using System.Text.Json;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Serialization;

namespace Rowles.LeanCorpus.Index;

/// <summary>
/// Lightweight crash recovery for LeanCorpus indices.
/// On startup: validates the latest commit, falls back to prior generations on corruption,
/// and cleans up orphaned segment files and temp files.
/// </summary>
public static class IndexRecovery
{
    /// <summary>
    /// Attempts to load the latest valid commit from the index directory.
    /// Tries generations from highest to lowest. Returns null if no valid commit exists.
    /// </summary>
    /// <param name="directoryPath">The index directory.</param>
    /// <param name="cleanupOrphans">
    /// When <c>true</c> (writer-side recovery), deletes orphan segment files and stale temp files.
    /// When <c>false</c> (reader-side polling), only inspects the directory and never mutates it —
    /// reader threads must not race the writer by deleting in-flight segment files.
    /// </param>
    public static RecoveryResult? RecoverLatestCommit(string directoryPath, bool cleanupOrphans = true)
    {
        // Clean up any leftover temp files from interrupted commits (writer-side only).
        if (cleanupOrphans)
        {
            CleanupTempFiles(directoryPath);
            PromotePendingCommits(directoryPath);
        }

        var commitFiles = FindCommitFiles(directoryPath);
        if (commitFiles.Count == 0)
            return null;

        // Try each commit from newest to oldest
        foreach (var (generation, filePath) in commitFiles)
        {
            var result = TryLoadCommit(directoryPath, filePath, generation);
            if (result is not null)
            {
                if (cleanupOrphans)
                    CleanupOrphanedSegments(directoryPath, result.SegmentIds);
                return result;
            }
        }

        // Commit files exist but none validated. The index is corrupt: refuse to open
        // silently as an empty index, which would mask data loss.
        throw new InvalidDataException(
            $"Index at '{directoryPath}' is corrupt: {commitFiles.Count} commit file(s) found but none reference a valid set of segment files.");
    }

    /// <summary>
    /// Enumerates all segments_N files, sorted by generation descending.
    /// </summary>
    private static List<(int Generation, string FilePath)> FindCommitFiles(string directoryPath)
    {
        var result = new List<(int, string)>();
        if (!Directory.Exists(directoryPath))
            return result;

        foreach (var file in Directory.GetFiles(directoryPath, "segments_*"))
        {
            var fileName = Path.GetFileName(file);
            // Skip temp files and pending commit files
            if (fileName.EndsWith(".tmp", StringComparison.Ordinal) ||
                fileName.EndsWith(".pending", StringComparison.Ordinal))
                continue;
            var genStr = fileName.AsSpan("segments_".Length);
            if (int.TryParse(genStr, out int gen))
                result.Add((gen, file));
        }

        // Sort descending by generation (newest first)
        result.Sort((a, b) => b.Item1.CompareTo(a.Item1));
        return result;
    }

    /// <summary>
    /// Required per-segment file extensions checked during recovery. A commit referencing a
    /// segment whose required files are missing or empty falls back to the prior generation.
    /// </summary>
    private static readonly string[] RequiredSegmentExtensions = [".seg", ".dic", ".pos", ".nrm", ".fdt", ".fdx"];

    /// <summary>
    /// Per-extension codec format for dual-format header validation.
    /// Files that exist but fail header validation cause the commit to be rejected.
    /// </summary>
    private static readonly (string Ext, ICodec<byte[]> Format)[] HeaderChecks =
    [
        (".dic", CodecFormats.TermDictionary),
        (".pos", CodecFormats.Postings),
        (".nrm", CodecFormats.Norms),
    ];

    /// <summary>
    /// Tries to load and validate a specific commit file.
    /// Returns null if the file is corrupt or references missing or unreadable segments.
    /// Validates the required per-segment files (.seg, .dic, .pos, .nrm) as well as any
    /// vector and HNSW files declared in the segment metadata.
    /// </summary>
    private static RecoveryResult? TryLoadCommit(string directoryPath, string commitFilePath, int generation)
    {
        try
        {
            var json = CommitFileFormat.TryReadJson(commitFilePath);
            if (json is null)
                return null;
            var commitData = JsonSerializer.Deserialize(json, LeanCorpusJsonContext.Default.CommitData);
            if (commitData is null || commitData.Segments is null)
                return null;

            try { commitData.Validate(); } catch (InvalidDataException) { return null; }

            if (commitData.Generation != generation)
                return null;

            var validSegments = new List<string>();
            foreach (var segId in commitData.Segments)
            {
                if (!ValidateSegment(directoryPath, segId))
                    return null;
                validSegments.Add(segId);
            }

            return new RecoveryResult
            {
                Generation = generation,
                ContentToken = commitData.ContentToken,
                SegmentIds = validSegments,
                CommitFilePath = commitFilePath,
                WasFallback = false
            };
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool ValidateSegment(string directoryPath, string segId)
    {
        try
        {
            var basePath = Path.Combine(directoryPath, segId);
            foreach (var ext in RequiredSegmentExtensions)
            {
                var path = basePath + ext;
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0)
                    return false;
            }

            var segInfo = Segment.SegmentInfo.ReadFrom(basePath + ".seg");

            foreach (var (ext, format) in HeaderChecks)
            {
                var path = basePath + ext;
                if (!File.Exists(path)) return false;
                using var fs = File.OpenRead(path);
                using var reader = new BinaryReader(fs);
                CodecFileHeader.ReadVersion(reader, format);
            }

            foreach (var vf in segInfo.VectorFields)
            {
                var vecPath = vf.Quantisation != Codecs.Vectors.VectorQuantisation.None
                    ? Codecs.Vectors.VectorFilePaths.QuantisedVectorFile(basePath, vf.FieldName)
                    : Codecs.Vectors.VectorFilePaths.VectorFile(basePath, vf.FieldName);
                if (!File.Exists(vecPath)) return false;
                if (vf.HasHnsw)
                {
                    var hnswPath = Codecs.Vectors.VectorFilePaths.HnswFile(basePath, vf.FieldName);
                    if (!File.Exists(hnswPath)) return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Promotes orphaned <c>segments_N.pending</c> files to full commits.
    /// An orphaned pending file (no corresponding <c>segments_N</c>) indicates a crash
    /// after <c>PrepareCommit</c> but before <c>Commit</c>. The prepared data is complete
    /// and should be recovered.
    /// </summary>
    private static void PromotePendingCommits(string directoryPath)
    {
        foreach (var pendingFile in Directory.GetFiles(directoryPath, "segments_*.pending"))
        {
            var fileName = Path.GetFileName(pendingFile);
            // Strip ".pending" suffix to get the target segments_N name.
            var finalName = fileName.Substring(0, fileName.Length - ".pending".Length);
            var finalPath = Path.Combine(directoryPath, finalName);

            if (!File.Exists(finalPath))
            {
                try { File.Move(pendingFile, finalPath); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            else
            {
                // Both .pending and final exist — the final commit won, discard the stale pending.
                try { File.Delete(pendingFile); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// Removes temp files left by interrupted write-then-rename commits.
    /// </summary>
    private static void CleanupTempFiles(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return;

        foreach (var tmpFile in Directory.GetFiles(directoryPath, "*.tmp"))
        {
            if (!IsRecognisedTemporaryFile(Path.GetFileName(tmpFile)))
                continue;

            try { File.Delete(tmpFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static bool IsRecognisedTemporaryFile(string fileName)
        => fileName is "migration_state.json.tmp"
           || (fileName.StartsWith("segments_", StringComparison.Ordinal) && fileName.EndsWith(".tmp", StringComparison.Ordinal))
           || (fileName.StartsWith("stats_", StringComparison.Ordinal) && fileName.EndsWith(".json.tmp", StringComparison.Ordinal))
           || fileName.EndsWith(".dic.tmp", StringComparison.Ordinal)
           || fileName.EndsWith(".pos.tmp", StringComparison.Ordinal)
           || fileName.EndsWith(".dvn.tmp", StringComparison.Ordinal)
           || fileName.EndsWith(".dvs.tmp", StringComparison.Ordinal)
           || fileName.EndsWith(".dss.tmp", StringComparison.Ordinal)
           || fileName.EndsWith(".dsn.tmp", StringComparison.Ordinal)
           || fileName.EndsWith(".dvb.tmp", StringComparison.Ordinal)
           || fileName.EndsWith(".fln.tmp", StringComparison.Ordinal)
           || fileName.EndsWith(".fdt.tmp", StringComparison.Ordinal)
           || fileName.EndsWith(".fdx.tmp", StringComparison.Ordinal)
           || fileName.EndsWith(".seg.tmp", StringComparison.Ordinal)
           || fileName.EndsWith(".stats.json.tmp", StringComparison.Ordinal)
           || (fileName.Contains("_gen_", StringComparison.Ordinal) && fileName.EndsWith(".del.tmp", StringComparison.Ordinal));

    /// <summary>
    /// Removes segment files that are not referenced by the active commit. Uses a
    /// pattern-based match so all sidecar files for the orphaned segment are cleaned,
    /// including stats, vector, and HNSW files that may have been added by later codecs.
    /// </summary>
    private static void CleanupOrphanedSegments(string directoryPath, List<string> activeSegmentIds)
    {
        var activeSet = new HashSet<string>(activeSegmentIds, StringComparer.Ordinal);

        // Find all segment IDs on disk by looking for .seg files
        foreach (var segFile in Directory.GetFiles(directoryPath, "*.seg"))
        {
            var segId = Path.GetFileNameWithoutExtension(segFile);
            if (activeSet.Contains(segId))
                continue;

            // Pattern: segId.* and segId_v_*.* (per-field vector and HNSW files).
            DeleteByPattern(directoryPath, segId + ".*");
            DeleteByPattern(directoryPath, segId + "_v_*.*");
        }
    }

    private static void DeleteByPattern(string directoryPath, string pattern)
    {
        foreach (var path in Directory.GetFiles(directoryPath, pattern))
        {
            try { File.Delete(path); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "orphan cleanup"); }
        }
    }

    /// <summary>Result of crash recovery.</summary>
    public sealed class RecoveryResult
    {
        /// <summary>Gets the generation number of the recovered commit.</summary>
        public int Generation { get; init; }

        /// <summary>Gets the logical content token stored in the recovered commit.</summary>
        public long ContentToken { get; init; }

        /// <summary>Gets the segment IDs referenced by the recovered commit.</summary>
        public List<string> SegmentIds { get; init; } = [];

        /// <summary>Gets the file path of the commit file that was successfully loaded.</summary>
        public string CommitFilePath { get; init; } = "";

        /// <summary>Gets a value indicating whether recovery fell back to an older commit generation.</summary>
        public bool WasFallback { get; init; }
    }
}

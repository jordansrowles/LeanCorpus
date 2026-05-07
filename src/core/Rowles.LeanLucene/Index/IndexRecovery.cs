using System.Text.Json;
using Rowles.LeanLucene.Serialization;

namespace Rowles.LeanLucene.Index;

/// <summary>
/// Lightweight crash recovery for LeanLucene indices.
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
            CleanupTempFiles(directoryPath);

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
            // Skip temp files
            if (fileName.EndsWith(".tmp", StringComparison.Ordinal))
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
    /// Per-extension expected codec magic header version. Files that exist but fail header
    /// validation cause the commit to be rejected and recovery to fall back.
    /// </summary>
    private static readonly (string Ext, byte Version, string FileType)[] HeaderChecks =
    [
        (".dic", Codecs.CodecConstants.TermDictionaryVersion, "term dictionary"),
        (".pos", Codecs.CodecConstants.PostingsVersion, "postings"),
        (".nrm", Codecs.CodecConstants.NormsVersion, "norms"),
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
                return null; // CRC mismatch — torn write
            var commitData = JsonSerializer.Deserialize(json, LeanLuceneJsonContext.Default.CommitData);
            if (commitData is null || commitData.Segments is null)
                return null;

            // Validate generation matches
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
            return null; // corrupt JSON
        }
        catch (IOException)
        {
            return null; // file read error
        }
    }

    /// <summary>
    /// Returns true if every required file for the given segment exists, is non-empty,
    /// and the optional vector and HNSW files declared in the segment metadata are present.
    /// </summary>
    private static bool ValidateSegment(string directoryPath, string segId)
    {
        var basePath = Path.Combine(directoryPath, segId);
        foreach (var ext in RequiredSegmentExtensions)
        {
            var path = basePath + ext;
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
                return false;
        }

        Segment.SegmentInfo segInfo;
        try
        {
            segInfo = Segment.SegmentInfo.ReadFrom(basePath + ".seg");
        }
        catch (Exception)
        {
            return false;
        }

        // Validate codec headers on the per-segment files that carry them.
        foreach (var (ext, version, fileType) in HeaderChecks)
        {
            var path = basePath + ext;
            if (!File.Exists(path)) return false;
            try
            {
                using var fs = File.OpenRead(path);
                using var reader = new BinaryReader(fs);
                Codecs.CodecConstants.ValidateHeader(reader, version, fileType);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // .del file presence is intentionally NOT validated here. A missing .del
        // is treated as a fully-live segment by the reader, which is the documented
        // graceful-degradation behaviour for fault-injection scenarios.

        foreach (var vf in segInfo.VectorFields)
        {
            var vecPath = Codecs.Vectors.VectorFilePaths.VectorFile(basePath, vf.FieldName);
            if (!File.Exists(vecPath)) return false;
            if (vf.HasHnsw)
            {
                var hnswPath = Codecs.Vectors.VectorFilePaths.HnswFile(basePath, vf.FieldName);
                if (!File.Exists(hnswPath)) return false;
            }
        }

        return true;
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
            try { File.Delete(tmpFile); } catch { /* best-effort */ }
        }
    }

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
            try { File.Delete(path); } catch { /* best-effort */ }
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

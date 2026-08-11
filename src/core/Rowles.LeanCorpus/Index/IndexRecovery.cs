using System.Text.Json;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;

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
    /// <param name="catalog">The immutable codec catalogue used for file validation and temporary-file recognition.</param>
    public static RecoveryResult? RecoverLatestCommit(
        string directoryPath,
        bool cleanupOrphans = true,
        CodecCatalog? catalog = null)
    {
        catalog ??= CodecCatalog.Default;

        // Clean up any leftover temp files from interrupted commits (writer-side only).
        if (cleanupOrphans)
        {
            CleanupTempFiles(directoryPath, catalog);
            PromotePendingCommits(directoryPath);
        }

        var commitFiles = FindCommitFiles(directoryPath);
        if (commitFiles.Count == 0)
            return null;

        // Try each commit from newest to oldest
        for (int commitIndex = 0; commitIndex < commitFiles.Count; commitIndex++)
        {
            var (generation, filePath) = commitFiles[commitIndex];
            var result = TryLoadCommit(directoryPath, filePath, generation, catalog, wasFallback: commitIndex > 0);
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
        if (!FileOpenRetry.DirectoryExists(directoryPath))
            return result;

        foreach (var file in FileOpenRetry.GetFiles(directoryPath, "segments_*"))
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
    /// Required logical codec files checked during recovery. Their descriptors are the authority
    /// for both current frames and supported historical frames.
    /// </summary>
    private static readonly RequiredSegmentFile[] RequiredSegmentFiles =
    [
        new(".dic"),
        new(".pos"),
        new(".nrm"),
        new(".fdt"),
        new(".fdx"),
    ];

    /// <summary>
    /// Tries to load and validate a specific commit file.
    /// Returns null if the file is corrupt or references missing or unreadable segments.
    /// Validates the required per-segment files (.seg, .dic, .pos, .nrm) as well as any
    /// vector and HNSW files declared in the segment metadata.
    /// </summary>
    private static RecoveryResult? TryLoadCommit(
        string directoryPath,
        string commitFilePath,
        int generation,
        CodecCatalog catalog,
        bool wasFallback)
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
                if (!ValidateSegment(directoryPath, segId, catalog))
                    return null;
                validSegments.Add(segId);
            }

            return new RecoveryResult
            {
                Generation = generation,
                ContentToken = commitData.ContentToken,
                SegmentIds = validSegments,
                CommitFilePath = commitFilePath,
                WasFallback = wasFallback
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

    private static bool ValidateSegment(string directoryPath, string segId, CodecCatalog catalog)
    {
        try
        {
            var basePath = Path.Combine(directoryPath, segId);
            var segInfo = Segment.SegmentInfo.ReadFrom(basePath + ".seg");

            using var directory = new MMapDirectory(directoryPath);
            using Segment.ISegmentFileSource source = segInfo.IsCompoundFile
                ? new Segment.CompoundSegmentFileSource(directory, segId)
                : new Segment.LooseSegmentFileSource(directory, segId);

            foreach (var required in RequiredSegmentFiles)
            {
                string fileName = segId + required.Extension;
                if (!source.FileExists(fileName) || source.GetFileLength(fileName) == 0)
                    return false;
            }

            EnsureVectorFilesExist(segInfo, source);

            foreach (var fileName in source.EnumerateFiles())
            {
                if (!catalog.TryMatchFile(fileName, out var descriptor) ||
                    descriptor?.CurrentFormatVersion is null)
                {
                    continue;
                }

                // BKD files are query accelerators backed by authoritative numeric indexes.
                // Their readers validate them and fall back to those indexes when corrupt.
                if (IsRecoverableQueryAccelerator(descriptor))
                    continue;

                ValidateCodecFile(source.OpenInput(fileName), descriptor, segInfo.DocCount);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
            or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static bool IsRecoverableQueryAccelerator(CodecFileDescriptor descriptor)
        => descriptor.FormatId is "leancorpus.numeric-structures.bkd"
            or "leancorpus.numeric-structures.int64-bkd";

    private static void EnsureVectorFilesExist(Segment.SegmentInfo segment, Segment.ISegmentFileSource source)
    {
        foreach (var vector in segment.VectorFields)
        {
            bool quantised = vector.Quantisation != VectorQuantisation.None;
            string vectorFile = quantised
                ? Path.GetFileName(VectorFilePaths.QuantisedVectorFile(segment.SegmentId, vector.FieldName))
                : Path.GetFileName(VectorFilePaths.VectorFile(segment.SegmentId, vector.FieldName));
            if (!source.FileExists(vectorFile))
                throw new InvalidDataException($"Segment '{segment.SegmentId}' is missing vector file '{vectorFile}'.");

            if (!vector.HasHnsw)
                continue;

            string hnswFile = Path.GetFileName(VectorFilePaths.HnswFile(segment.SegmentId, vector.FieldName));
            if (!source.FileExists(hnswFile))
                throw new InvalidDataException($"Segment '{segment.SegmentId}' is missing HNSW file '{hnswFile}'.");
        }
    }

    private static void ValidateCodecFile(IndexInput input, CodecFileDescriptor descriptor, int maxDoc)
    {
        using var inputLifetime = input;
        if (HasCanonicalFrameMagic(input))
        {
            using var canonical = CodecFileReader.Open(input, descriptor);
            canonical.ValidateChecksum();
            return;
        }

        if (descriptor.SupportedVersions.Any(static version =>
                version.IsReadable && (version.LegacyFraming & CodecLegacyFraming.Headerless) != 0))
        {
            switch (descriptor.FormatId)
            {
                case "leancorpus.numeric-structures.numeric-index":
                    _ = NumericIndexCodec.ReadDouble(input);
                    return;
                case "leancorpus.numeric-structures.int64-numeric-index":
                    _ = NumericIndexCodec.ReadInt64(input);
                    return;
                case "leancorpus.deletes.live-docs":
                    _ = Segment.LiveDocs.Deserialise(input, maxDoc);
                    return;
                case "leancorpus.deletes.parent-bitset":
                    _ = Segment.ParentBitSet.ReadFrom(input);
                    return;
                default:
                    if (descriptor.ValidationHandler is null)
                    {
                        throw new InvalidDataException(
                            $"Headerless codec format '{descriptor.FormatId}' has no recovery validation handler.");
                    }
                    descriptor.ValidationHandler.Validate(input);
                    return;
            }
        }

        switch (descriptor.FormatId)
        {
            case "leancorpus.postings.data":
                ValidateLegacyVersion(PostingsFileHeader.ReadVersion(input), descriptor);
                return;
            case "leancorpus.stored-fields.data":
                using (var data = StoredFieldsCodecFiles.OpenData(input))
                    ValidateLegacyVersion(data.Version, descriptor);
                return;
            case "leancorpus.stored-fields.index":
                using (var index = StoredFieldsCodecFiles.OpenIndex(input))
                    ValidateLegacyVersion(index.Version, descriptor);
                return;
            default:
                using (var legacy = LegacyCodecFileReader.Open(input, descriptor))
                    ValidateLegacyVersion(legacy.Metadata.FormatVersion, descriptor);
                return;
        }
    }

    private static bool HasCanonicalFrameMagic(IndexInput input)
    {
        if (input.Length - input.Position < sizeof(int))
            return false;

        long start = input.Position;
        uint magic = unchecked((uint)input.ReadInt32());
        input.Seek(start);
        return magic == CodecFileWriter.Magic;
    }

    private static void ValidateLegacyVersion(int version, CodecFileDescriptor descriptor)
    {
        if (!descriptor.SupportedVersions.Any(candidate => candidate.Version == version && candidate.IsReadable))
        {
            throw new InvalidDataException(
                $"Codec format '{descriptor.FormatId}' uses unreadable legacy version {version}.");
        }
    }

    private sealed record RequiredSegmentFile(string Extension);

    /// <summary>
    /// Promotes orphaned <c>segments_N.pending</c> files to full commits.
    /// An orphaned pending file (no corresponding <c>segments_N</c>) indicates a crash
    /// after <c>PrepareCommit</c> but before <c>Commit</c>. The prepared data is complete
    /// and should be recovered.
    /// </summary>
    private static void PromotePendingCommits(string directoryPath)
    {
        foreach (var pendingFile in FileOpenRetry.GetFiles(directoryPath, "segments_*.pending"))
        {
            var fileName = Path.GetFileName(pendingFile);
            // Strip ".pending" suffix to get the target segments_N name.
            var finalName = fileName.Substring(0, fileName.Length - ".pending".Length);
            var finalPath = Path.Combine(directoryPath, finalName);

            if (!FileOpenRetry.FileExists(finalPath))
            {
                try { FileOpenRetry.Move(pendingFile, finalPath); }
                catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "pending commit promote move"); }
            }
            else
            {
                // Both .pending and final exist — the final commit won, discard the stale pending.
                try { FileOpenRetry.Delete(pendingFile); }
                catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "stale pending file delete"); }
            }
        }
    }

    /// <summary>
    /// Removes temp files left by interrupted write-then-rename commits.
    /// </summary>
    private static void CleanupTempFiles(string directoryPath, CodecCatalog catalog)
    {
        if (!FileOpenRetry.DirectoryExists(directoryPath))
            return;

        foreach (var tmpFile in FileOpenRetry.GetFiles(directoryPath, "*.tmp"))
        {
            if (!IsRecognisedTemporaryFile(Path.GetFileName(tmpFile), catalog))
                continue;

            try { FileOpenRetry.Delete(tmpFile); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "temp file cleanup"); }
        }
    }

    private static bool IsRecognisedTemporaryFile(string fileName, CodecCatalog catalog)
        => catalog.TryMatchTemporaryFile(fileName, out _);

    /// <summary>
    /// Removes segment files that are not referenced by the active commit. Uses a
    /// pattern-based match so all sidecar files for the orphaned segment are cleaned,
    /// including stats, vector, and HNSW files that may have been added by later codecs.
    /// </summary>
    private static void CleanupOrphanedSegments(string directoryPath, List<string> activeSegmentIds)
    {
        var activeSet = new HashSet<string>(activeSegmentIds, StringComparer.Ordinal);

        // Find all segment IDs on disk by looking for .seg files
        foreach (var segFile in FileOpenRetry.GetFiles(directoryPath, "*.seg"))
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
        foreach (var path in FileOpenRetry.GetFiles(directoryPath, pattern))
        {
            try { FileOpenRetry.Delete(path); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "orphan cleanup"); }
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

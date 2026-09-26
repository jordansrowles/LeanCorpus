using System.Globalization;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>Describes the physical files owned by one segment.</summary>
internal sealed class SegmentFileSet
{
    private const string SegmentMetadataFormatId = "leancorpus.segment-store.metadata";
    private const string SegmentStatisticsFormatId = "leancorpus.segment-store.statistics";
    private const string CompoundSegmentFormatId = "leancorpus.segment-store.compound";
    private const string LiveDocsFormatId = "leancorpus.deletes.live-docs";
    private const string ParentBitSetFormatId = "leancorpus.deletes.parent-bitset";
    private const string VectorFamilyId = "leancorpus.vectors";

    private static readonly IReadOnlyDictionary<string, string> BackupRoles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["leancorpus.term-dictionary.data"] = "term-dictionary",
            ["leancorpus.postings.data"] = "postings",
            ["leancorpus.norms.data"] = "norms",
            ["leancorpus.field-lengths.data"] = "field-length",
            ["leancorpus.numeric-structures.numeric-index"] = "numeric-field-index",
            ["leancorpus.numeric-structures.bkd"] = "bkd",
            ["leancorpus.doc-values.numeric"] = "numeric-doc-values",
            ["leancorpus.doc-values.sorted"] = "sorted-doc-values",
            ["leancorpus.doc-values.sorted-set"] = "sorted-set-doc-values",
            ["leancorpus.doc-values.sorted-numeric"] = "sorted-numeric-doc-values",
            ["leancorpus.doc-values.binary"] = "binary-doc-values",
            ["leancorpus.stored-fields.data"] = "stored-fields",
            ["leancorpus.stored-fields.index"] = "stored-fields",
            ["leancorpus.term-vectors.data"] = "term-vector-data",
            ["leancorpus.term-vectors.index"] = "term-vector-index",
            [ParentBitSetFormatId] = "parent-bitset",
            [LiveDocsFormatId] = "live-docs",
            ["leancorpus.vectors.float32"] = "vector",
            ["leancorpus.vectors.hnsw"] = "hnsw",
            [SegmentMetadataFormatId] = "segment-metadata",
            [CompoundSegmentFormatId] = "compound-segment",
            [SegmentStatisticsFormatId] = "segment-stats",
        };

    private SegmentFileSet(string segmentId, SegmentFileEntry[] files)
    {
        SegmentId = segmentId;
        Files = Array.AsReadOnly(files);
    }

    internal string SegmentId { get; }

    internal IReadOnlyList<SegmentFileEntry> Files { get; }

    internal IEnumerable<string> FileNames => Files.Select(static file => file.FileName);

    internal IEnumerable<string> ImmutableCodecFileNames
        => Files.Where(static file => file.IsImmutableCodec).Select(static file => file.FileName);

    internal static SegmentFileSet Enumerate(
        string directoryPath,
        string segmentId,
        CodecCatalog? catalog = null,
        bool includeTemporary = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        return FromFileNames(
            segmentId,
            FileOpenRetry.EnumerateFiles(directoryPath, "*"),
            catalog,
            includeTemporary);
    }

    internal static SegmentFileSet FromFileNames(
        string segmentId,
        IEnumerable<string> fileNames,
        CodecCatalog? catalog = null,
        bool includeTemporary = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentId);
        ArgumentNullException.ThrowIfNull(fileNames);
        catalog ??= CodecCatalog.Default;

        var files = new List<SegmentFileEntry>();
        foreach (string pathOrName in fileNames)
        {
            string fileName = Path.GetFileName(pathOrName);
            if (string.IsNullOrEmpty(fileName) || !IsOwnedFileName(segmentId, fileName))
                continue;
            if (!includeTemporary && IsTemporaryFileName(fileName, catalog))
                continue;

            files.Add(Classify(segmentId, fileName, catalog));
        }

        files.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.FileName, right.FileName));
        return new SegmentFileSet(segmentId, files.ToArray());
    }

    /// <summary>Returns whether a physical name falls on a segment sidecar boundary.</summary>
    internal static bool IsOwnedFileName(string segmentId, string fileName)
        => fileName.StartsWith(segmentId + ".", StringComparison.Ordinal)
            || fileName.StartsWith(segmentId + "_v_", StringComparison.Ordinal)
            || fileName.StartsWith(segmentId + "_gen_", StringComparison.Ordinal);

    internal static bool IsOwnedByAnySegment(string fileName, IReadOnlySet<string> segmentIds)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(segmentIds);

        int boundary = fileName.Length;
        int dot = fileName.IndexOf('.');
        int vectorMarker = fileName.IndexOf("_v_", StringComparison.Ordinal);
        int deletionMarker = fileName.IndexOf("_gen_", StringComparison.Ordinal);
        if (dot >= 0)
            boundary = Math.Min(boundary, dot);
        if (vectorMarker >= 0)
            boundary = Math.Min(boundary, vectorMarker);
        if (deletionMarker >= 0)
            boundary = Math.Min(boundary, deletionMarker);
        if (boundary <= 0)
            return false;

        string candidate = fileName[..boundary];
        return segmentIds.Contains(candidate) && IsOwnedFileName(candidate, fileName);
    }

    internal static bool IsTemporaryFileName(string fileName, CodecCatalog? catalog = null)
    {
        if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return true;
        catalog ??= CodecCatalog.Default;
        return catalog.TryMatchTemporaryFile(fileName, out _);
    }

    /// <summary>Finds segment IDs from metadata and known sidecars, including files left without a .seg.</summary>
    internal static IReadOnlyList<string> FindSegmentIds(IEnumerable<string> fileNames, CodecCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(fileNames);
        catalog ??= CodecCatalog.Default;
        var segmentIds = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string pathOrName in fileNames)
        {
            string fileName = Path.GetFileName(pathOrName);
            if (string.IsNullOrEmpty(fileName) || IsTemporaryFileName(fileName, catalog))
                continue;
            if (TryGetSegmentId(fileName, catalog, out string? segmentId) && segmentId is not null)
                segmentIds.Add(segmentId);
        }

        return segmentIds.ToArray();
    }

    internal static bool TryGetSegmentId(string fileName, CodecCatalog catalog, out string? segmentId)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(catalog);
        fileName = Path.GetFileName(fileName);
        segmentId = null;
        if (!catalog.TryMatchFile(fileName, out CodecFileDescriptor? descriptor) || descriptor is null)
            return false;

        if (descriptor.FamilyId == "leancorpus.segment-store"
            && descriptor.FormatId is not (SegmentMetadataFormatId or SegmentStatisticsFormatId or CompoundSegmentFormatId))
        {
            return false;
        }

        if (descriptor.FamilyId == VectorFamilyId)
        {
            int vectorMarker = fileName.IndexOf("_v_", StringComparison.Ordinal);
            if (vectorMarker <= 0)
                return false;
            segmentId = fileName[..vectorMarker];
            return true;
        }

        if (descriptor.FormatId == LiveDocsFormatId)
        {
            if (fileName.EndsWith(".del", StringComparison.OrdinalIgnoreCase))
            {
                int generationMarker = fileName.IndexOf("_gen_", StringComparison.Ordinal);
                if (generationMarker > 0)
                    segmentId = fileName[..generationMarker];
                else
                    segmentId = fileName[..^".del".Length];
                return segmentId.Length > 0;
            }
            return false;
        }

        int suffixStart = FindMatchingSuffixStart(fileName, descriptor.FileMatcher);
        if (suffixStart <= 0)
            return false;
        segmentId = fileName[..suffixStart];
        return true;
    }

    /// <summary>Deletes every physical file owned by this segment, including unknown sidecars and generations.</summary>
    internal void DeleteAllOwnedFiles(LeanDirectory directory, string operation)
    {
        ArgumentNullException.ThrowIfNull(directory);
        foreach (SegmentFileEntry file in Files)
            TryDelete(directory, file.FileName, operation);
    }

    /// <summary>Deletes deletion files not selected by any active commit or held snapshot state.</summary>
    internal void PruneDeletionFiles(
        LeanDirectory directory,
        IReadOnlySet<int?> protectedGenerations,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(protectedGenerations);
        foreach (SegmentFileEntry file in Files)
        {
            bool unprotectedGeneration = file.Kind == SegmentFileKind.DeletionGeneration
                && !protectedGenerations.Contains(file.DeletionGeneration);
            bool unprotectedLegacy = file.Kind == SegmentFileKind.LegacyDeletionFile
                && !protectedGenerations.Contains(null);
            if (unprotectedGeneration || unprotectedLegacy)
                TryDelete(directory, file.FileName, operation);
        }
    }

    /// <summary>Returns the stable manifest role for a file using the supplied codec catalogue.</summary>
    internal static string GetBackupRole(string fileName, CodecCatalog? catalog = null)
    {
        catalog ??= CodecCatalog.Default;
        return catalog.TryMatchFile(fileName, out CodecFileDescriptor? descriptor)
            && descriptor is not null
            && BackupRoles.TryGetValue(descriptor.FormatId, out string? role)
                ? role
                : "sidecar";
    }

    private static SegmentFileEntry Classify(string segmentId, string fileName, CodecCatalog catalog)
    {
        if (!catalog.TryMatchFile(fileName, out CodecFileDescriptor? descriptor) || descriptor is null)
            return new SegmentFileEntry(fileName, null, SegmentFileKind.Unknown, null);

        if (descriptor.FormatId == LiveDocsFormatId)
        {
            if (TryParseDeletionGeneration(fileName, segmentId, out int generation))
                return new SegmentFileEntry(fileName, descriptor, SegmentFileKind.DeletionGeneration, generation);
            if (fileName.Equals(segmentId + ".del", StringComparison.Ordinal))
                return new SegmentFileEntry(fileName, descriptor, SegmentFileKind.LegacyDeletionFile, null);
            return new SegmentFileEntry(fileName, descriptor, SegmentFileKind.Unknown, null);
        }

        if (descriptor.FormatId == SegmentMetadataFormatId)
            return new SegmentFileEntry(fileName, descriptor, SegmentFileKind.Metadata, null);
        if (descriptor.FormatId == SegmentStatisticsFormatId)
            return new SegmentFileEntry(fileName, descriptor, SegmentFileKind.Statistics, null);
        if (descriptor.FormatId == CompoundSegmentFormatId)
            return new SegmentFileEntry(fileName, descriptor, SegmentFileKind.CompoundContainer, null);
        if (descriptor.FormatId == ParentBitSetFormatId)
            return new SegmentFileEntry(fileName, descriptor, SegmentFileKind.ImmutableCodec, null);
        if (descriptor.FamilyId == VectorFamilyId)
            return new SegmentFileEntry(fileName, descriptor, SegmentFileKind.VectorCodec, null);
        if (descriptor.FamilyId == "leancorpus.segment-store" || descriptor.FamilyId == "leancorpus.deletes")
            return new SegmentFileEntry(fileName, descriptor, SegmentFileKind.Unknown, null);
        return new SegmentFileEntry(fileName, descriptor, SegmentFileKind.ImmutableCodec, null);
    }

    private static bool TryParseDeletionGeneration(string fileName, string segmentId, out int generation)
    {
        generation = 0;
        string prefix = segmentId + "_gen_";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal)
            || !fileName.EndsWith(".del", StringComparison.OrdinalIgnoreCase))
            return false;

        ReadOnlySpan<char> value = fileName.AsSpan(prefix.Length, fileName.Length - prefix.Length - ".del".Length);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out generation);
    }

    private static int FindMatchingSuffixStart(string fileName, CodecFileMatcher matcher)
    {
        for (int i = 0; i < fileName.Length; i++)
        {
            if (fileName[i] == '.' && matcher.IsMatch(fileName[i..]))
                return i;
        }
        return -1;
    }

    private static void TryDelete(LeanDirectory directory, string fileName, string operation)
    {
        try
        {
            directory.DeleteFile(fileName);
        }
        catch (Exception ex)
        {
            Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, operation);
        }
    }
}

internal enum SegmentFileKind
{
    ImmutableCodec,
    VectorCodec,
    DeletionGeneration,
    LegacyDeletionFile,
    Metadata,
    Statistics,
    CompoundContainer,
    Unknown,
}

internal readonly record struct SegmentFileEntry(
    string FileName,
    CodecFileDescriptor? Descriptor,
    SegmentFileKind Kind,
    int? DeletionGeneration)
{
    internal bool IsImmutableCodec => Kind is SegmentFileKind.ImmutableCodec or SegmentFileKind.VectorCodec;
}

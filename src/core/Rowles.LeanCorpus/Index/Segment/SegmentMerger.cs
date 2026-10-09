using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment.Merging;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Tiered merge policy. When the number of segments at a given size tier
/// exceeds a configurable threshold, the smallest segments in that tier
/// are merged into one. Old segments are removed only after the merged
/// segment is fully committed.
/// </summary>
public sealed class SegmentMerger
{
    private readonly MMapDirectory _directory;
    private readonly IMergePolicy _mergePolicy;
    private readonly int _skipInterval;
    private readonly double _softDeleteRetentionSeconds;
    private readonly Diagnostics.IMetricsCollector _metrics;
    private readonly HnswBuildConfig _hnswBuildConfig;
    private readonly bool _useCompoundFile;
    private readonly VectorQuantisation _destinationVectorQuantisation;

    internal CodecCatalog FileCatalog { get; set; } = CodecCatalog.Default;

    /// <summary>Default merge threshold: when this many segments exist, merge.</summary>
    public const int DefaultMergeThreshold = 10;

    /// <summary>Default postings skip interval.</summary>
    public const int DefaultSkipInterval = 128;

    /// <summary>Default soft-delete retention period in seconds (24 hours).</summary>
    public const double DefaultSoftDeleteRetentionSeconds = 86400.0;

    /// <summary>Initialises a merger bound to the given directory.</summary>
    /// <param name="directory">The directory holding segment files.</param>
    /// <param name="mergePolicy">The merge policy used to select segments for merging.</param>
    /// <param name="skipInterval">Postings skip interval used when writing the merged segment.</param>
    /// <param name="softDeleteRetentionSeconds">Minimum seconds to retain soft-deleted documents during merge.</param>
    /// <param name="hnswBuildConfig">HNSW build configuration used when rebuilding vector graphs during merge.</param>
    /// <param name="metrics">Optional metrics collector. Defaults to <see cref="Diagnostics.NullMetricsCollector.Instance"/>.</param>
    /// <param name="useCompoundFile">Whether merged immutable codec files should be packed into a compound file.</param>
    public SegmentMerger(
        MMapDirectory directory,
        IMergePolicy mergePolicy,
        int skipInterval = DefaultSkipInterval,
        double softDeleteRetentionSeconds = DefaultSoftDeleteRetentionSeconds,
        HnswBuildConfig? hnswBuildConfig = null,
        Diagnostics.IMetricsCollector? metrics = null,
        bool useCompoundFile = false)
        : this(directory, mergePolicy, skipInterval, softDeleteRetentionSeconds, hnswBuildConfig,
            metrics, useCompoundFile, VectorQuantisation.None)
    {
    }

    internal SegmentMerger(
        MMapDirectory directory,
        IMergePolicy mergePolicy,
        int skipInterval,
        double softDeleteRetentionSeconds,
        HnswBuildConfig? hnswBuildConfig,
        Diagnostics.IMetricsCollector? metrics,
        bool useCompoundFile,
        VectorQuantisation destinationVectorQuantisation)
    {
        _directory = directory;
        _mergePolicy = mergePolicy ?? new TieredMergePolicy(DefaultMergeThreshold);
        _skipInterval = skipInterval;
        _softDeleteRetentionSeconds = softDeleteRetentionSeconds;
        _hnswBuildConfig = hnswBuildConfig ?? new HnswBuildConfig();
        _metrics = metrics ?? Diagnostics.NullMetricsCollector.Instance;
        _useCompoundFile = useCompoundFile;
        _destinationVectorQuantisation = destinationVectorQuantisation;
    }

    internal SegmentMerger(
        MMapDirectory directory,
        IMergePolicy mergePolicy,
        int skipInterval,
        double softDeleteRetentionSeconds,
        HnswBuildConfig? hnswBuildConfig,
        bool useCompoundFile,
        VectorQuantisation destinationVectorQuantisation)
        : this(directory, mergePolicy, skipInterval, softDeleteRetentionSeconds, hnswBuildConfig,
            metrics: null,
            useCompoundFile: useCompoundFile,
            destinationVectorQuantisation: destinationVectorQuantisation)
    {
    }

    /// <summary>Initialises a merger bound to the given directory with the default tiered policy.</summary>
    /// <param name="directory">The directory holding segment files.</param>
    /// <param name="mergeThreshold">Number of segments at one tier before a merge is triggered.</param>
    /// <param name="skipInterval">Postings skip interval used when writing the merged segment.</param>
    /// <param name="softDeleteRetentionSeconds">Minimum seconds to retain soft-deleted documents during merge.</param>
    /// <param name="hnswBuildConfig">HNSW build configuration used when rebuilding vector graphs during merge.</param>
    /// <param name="metrics">Optional metrics collector. Defaults to <see cref="Diagnostics.NullMetricsCollector.Instance"/>.</param>
    public SegmentMerger(
        MMapDirectory directory,
        int mergeThreshold,
        int skipInterval = DefaultSkipInterval,
        double softDeleteRetentionSeconds = DefaultSoftDeleteRetentionSeconds,
        HnswBuildConfig? hnswBuildConfig = null,
        Diagnostics.IMetricsCollector? metrics = null)
        : this(directory, new TieredMergePolicy(mergeThreshold), skipInterval, softDeleteRetentionSeconds, hnswBuildConfig, metrics)
    {
    }

    /// <summary>
    /// Checks if a merge is needed and performs it. Returns the updated segment list.
    /// </summary>
    public List<SegmentInfo> MaybeMerge(List<SegmentInfo> segments, ref int nextSegmentOrdinal)
        => MaybeMerge(segments, ref nextSegmentOrdinal, new HashSet<string>(StringComparer.Ordinal), commitGeneration: 0);

    /// <summary>
    /// Checks if a merge is needed and performs it, excluding segments protected by held snapshots.
    /// </summary>
    /// <param name="segments">The committed segments currently visible to the writer.</param>
    /// <param name="nextSegmentOrdinal">The next segment ordinal to allocate if a merge is performed.</param>
    /// <param name="protectedSegmentIds">Segment IDs that must not be merged or deleted while snapshots are held.</param>
    /// <param name="commitGeneration">The commit generation to assign to the merged segment.</param>
    /// <returns>The original list when no merge is needed; otherwise, a new list containing merged replacements.</returns>
    public List<SegmentInfo> MaybeMerge(
        List<SegmentInfo> segments,
        ref int nextSegmentOrdinal,
        IReadOnlySet<string> protectedSegmentIds,
        int commitGeneration = 0)
    {
        var result = new List<SegmentInfo>(segments);
        bool anyMerged = false;

        while (true)
        {
            var toMerge = _mergePolicy.FindMerges(result, protectedSegmentIds);
            if (toMerge.Count < 2)
                break;

            // Merge policies select candidates by size and deletion density, but document IDs
            // must follow the committed segment order rather than an unstable policy sort.
            var selectedIds = new HashSet<string>(
                toMerge.Select(static segment => segment.SegmentId),
                StringComparer.Ordinal);
            var orderedForMerge = result.Where(segment => selectedIds.Contains(segment.SegmentId)).ToList();
            var merged = MergeSegments(
                orderedForMerge,
                ref nextSegmentOrdinal, commitGeneration);
            if (merged == null)
                break;

            foreach (var seg in toMerge)
                result.Remove(seg);
            result.Add(merged);
            anyMerged = true;
        }

        return anyMerged ? result : segments;
    }

    /// <summary>
    /// Forces a full merge of all given segments into a single new segment,
    /// bypassing tier-based merge policy. Used by <see cref="IndexWriter.Compact"/>.
    /// </summary>
    /// <param name="segments">All segments to merge into one.</param>
    /// <param name="nextSegmentOrdinal">Ordinal counter for naming the output segment.</param>
    /// <param name="commitGeneration">The commit generation to assign to the merged segment.</param>
    /// <returns>The merged segment, or <c>null</c> if no live documents remain.</returns>
    public SegmentInfo? MergeAll(List<SegmentInfo> segments, ref int nextSegmentOrdinal, int commitGeneration = 0)
    {
        if (segments.Count == 0)
            return null;

        return MergeSegments(segments, ref nextSegmentOrdinal, commitGeneration);
    }

    private SegmentInfo? MergeSegments(List<SegmentInfo> segments, ref int nextSegmentOrdinal, int commitGeneration)
    {
        // Open one SegmentReader per source segment up front and keep it open for the
        // whole merge. The merge has three passes (doc-id remap, field copy, norm copy)
        // and previously each opened its own SegmentReader, tripling mmap creation and
        // file-handle pressure.
        var readers = new Dictionary<string, SegmentReader>(StringComparer.Ordinal);
        try
        {
            foreach (var segInfo in segments)
                readers[segInfo.SegmentId] = new SegmentReader(_directory, segInfo, FileCatalog);

            var spatialFields = SegmentMergePlanner.ResolveSpatialFieldMetadata(segments, readers);
            var newSegId = $"seg_{nextSegmentOrdinal++}";
            var basePath = Path.Combine(_directory.DirectoryPath, newSegId);
            return MergeSegmentsCore(segments, readers, newSegId, basePath, commitGeneration,
                _destinationVectorQuantisation, spatialFields);
        }
        finally
        {
            foreach (var r in readers.Values)
                r.Dispose();
        }
    }

    private SegmentInfo? MergeSegmentsCore(
        List<SegmentInfo> segments, IReadOnlyDictionary<string, SegmentReader> readers,
        string newSegId, string basePath, int commitGeneration, VectorQuantisation destinationVectorQuantisation,
        List<SpatialFieldInfo> spatialFields)
    {
        var plan = SegmentMergePlanner.Build(segments, readers, newSegId, basePath,
            commitGeneration, _softDeleteRetentionSeconds, destinationVectorQuantisation, spatialFields);
        if (plan is null) return null;
        try
        {
            SegmentMergePostingsWriter.Write(plan, FileCatalog);
            var columns = new SegmentMergeDocValuesWriter.State(plan.TotalDocs);
            var vectors = new SegmentMergeVectorWriter.State(plan.TotalDocs);
            using var spatial = new SegmentMergeSpatialWriter.State(plan.TotalDocs);
            SegmentMergeStoredPayloadWriter.Write(plan, FileCatalog, columns, vectors, spatial);
            SegmentMergeDocValuesWriter.Write(plan, columns);
            var vectorWriter = new SegmentMergeVectorWriter(_directory, _hnswBuildConfig, _metrics);
            var vectorFields = vectorWriter.Write(plan, vectors);
            SegmentMergeSpatialWriter.Write(plan, columns, spatial);
            return SegmentMergeFinaliser.Finish(plan, vectorFields, _directory, FileCatalog, _useCompoundFile);
        }
        catch
        {
            SegmentMergeFinaliser.Cleanup(_directory, plan.SegmentId, FileCatalog);
            throw;
        }
    }

    internal void CleanupSegmentFiles(SegmentInfo seg)
    {
        SegmentFileSet.Enumerate(_directory.DirectoryPath, seg.SegmentId, FileCatalog)
            .DeleteAllOwnedFiles(_directory, "merge segment file cleanup");
    }

    /// <summary>
    /// Merges segments from a foreign directory into a single new segment in the target directory.
    /// Used by <see cref="IndexWriter.AddIndexes"/>.
    /// </summary>
    public SegmentInfo? MergeSegmentsFromDirectory(
        MMapDirectory sourceDirectory,
        List<SegmentInfo> sourceSegments,
        ref int nextSegmentOrdinal,
        IndexWriterConfig config,
        int commitGeneration = 0)
    {
        var readers = new Dictionary<string, SegmentReader>(StringComparer.Ordinal);
        try
        {
            foreach (var segInfo in sourceSegments)
                readers[segInfo.SegmentId] = new SegmentReader(sourceDirectory, segInfo, config.CodecCatalog);

            var spatialFields = SegmentMergePlanner.ResolveSpatialFieldMetadata(sourceSegments, readers);
            var newSegId = $"seg_{nextSegmentOrdinal++}";
            var basePath = Path.Combine(_directory.DirectoryPath, newSegId);
            return MergeSegmentsCore(sourceSegments, readers, newSegId, basePath, commitGeneration,
                config.VectorQuantisation, spatialFields);
        }
        finally
        {
            foreach (var r in readers.Values)
                r.Dispose();
        }
    }

}

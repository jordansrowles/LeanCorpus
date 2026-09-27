namespace Rowles.LeanCorpus.Diagnostics;

/// <summary>Reports retained resource estimates for an IndexSearcher segment-reader cache.</summary>
public readonly record struct SegmentReaderCacheMetricsSnapshot
{
    /// <summary>Gets the number of heavy segment-reader states currently retained.</summary>
    public int EntryCount { get; init; }

    /// <summary>Gets the number of states evicted since the searcher was created.</summary>
    public long EvictionCount { get; init; }

    /// <summary>Gets the current estimated retained bytes across cached reader states.</summary>
    public long RetainedBytes { get; init; }

    /// <summary>Gets the configured retained-byte limit for this searcher.</summary>
    public long MaximumRetainedBytes { get; init; }

    /// <summary>Gets estimated bytes retained by postings and term readers and offsets.</summary>
    public long PostingsAndTermsBytes { get; init; }

    /// <summary>Gets estimated bytes retained by norms and field lengths.</summary>
    public long NormsAndLengthsBytes { get; init; }

    /// <summary>Gets estimated bytes retained by stored-field readers.</summary>
    public long StoredFieldsBytes { get; init; }

    /// <summary>Gets estimated bytes retained by term-vector readers.</summary>
    public long TermVectorsBytes { get; init; }

    /// <summary>Gets estimated bytes retained by DocValues readers and materialised values.</summary>
    public long DocValuesBytes { get; init; }

    /// <summary>Gets estimated bytes retained by materialised numeric range indexes.</summary>
    public long NumericIndexesBytes { get; init; }

    /// <summary>Gets estimated bytes retained by BKD and shape readers.</summary>
    public long SpatialIndexesBytes { get; init; }

    /// <summary>Gets estimated bytes retained by vector readers and HNSW graphs.</summary>
    public long VectorsBytes { get; init; }

    /// <summary>Gets estimated bytes retained by live-document and parent bitsets.</summary>
    public long LiveDocsAndParentsBytes { get; init; }

    /// <summary>Gets estimated per-state reader-shell and cache overhead.</summary>
    public long OtherBytes { get; init; }
}

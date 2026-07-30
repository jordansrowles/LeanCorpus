namespace Rowles.LeanCorpus.Diagnostics;

/// <summary>Candidate-generation strategy used for one segment of a vector search.</summary>
public enum VectorExecutionStrategy
{
    /// <summary>Exact scan of all live vectors.</summary>
    ExactFlatScan,

    /// <summary>Exact scan of an eligible filtered set.</summary>
    ExactFilterScan,

    /// <summary>HNSW traversal constrained by an allow-list.</summary>
    HnswAllowList,

    /// <summary>HNSW traversal followed by post-filter retries.</summary>
    HnswPostFilter,

    /// <summary>Unfiltered HNSW traversal.</summary>
    Hnsw,

}

/// <summary>Stored precision used to produce final vector scores.</summary>
public enum VectorScorePrecision
{
    /// <summary>Scores came from Float32 vectors or retained Float32 sidecars.</summary>
    ExactFloat32,

    /// <summary>Scores came from reconstructed quantised vectors.</summary>
    ReconstructedQuantised,
}

/// <summary>Measured vector execution facts for one searched segment.</summary>
public readonly record struct VectorExecutionMetrics(
    VectorExecutionStrategy Strategy,
    VectorScorePrecision ScorePrecision,
    bool ExactCandidateSet,
    int CandidateCount,
    int EligibleCount,
    TimeSpan CandidateGenerationElapsed,
    TimeSpan RerankingElapsed);

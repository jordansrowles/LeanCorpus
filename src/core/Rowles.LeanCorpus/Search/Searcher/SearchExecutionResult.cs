using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>How a search completed.</summary>
public enum SearchCompletionState
{
    /// <summary>The configured execution completed without exhausting a budget.</summary>
    Completed,

    /// <summary>A configured work or time budget stopped execution.</summary>
    BudgetExhausted,

    /// <summary>The supplied cancellation token stopped execution.</summary>
    Cancelled,
}

/// <summary>Candidate-generation strategy reported for a search.</summary>
public enum SearchExecutionStrategy
{
    /// <summary>Ordinary non-vector query execution.</summary>
    Standard,

    /// <summary>Exact flat vector scan.</summary>
    VectorFlatScan,

    /// <summary>Approximate HNSW candidate generation.</summary>
    VectorHnsw,

    /// <summary>Segments used a mixture of flat scan and HNSW.</summary>
    VectorMixed,

    /// <summary>Multiple independently ranked children were fused.</summary>
    Fusion,
}

/// <summary>Precision source used for final vector scores.</summary>
public enum VectorScoreProvenance
{
    /// <summary>The query did not produce vector scores.</summary>
    NotApplicable,

    /// <summary>Scores were computed from stored Float32 vectors.</summary>
    ExactFloat32,

    /// <summary>Scores were computed from reconstructed quantised vectors.</summary>
    ReconstructedQuantised,

    /// <summary>Different segments used exact and reconstructed vector scores.</summary>
    Mixed,
}

/// <summary>Bounded execution facts for one search.</summary>
public sealed record SearchExecutionDiagnostics
{
    /// <summary>Completion state.</summary>
    public required SearchCompletionState Completion { get; init; }

    /// <summary>Selected candidate-generation strategy.</summary>
    public required SearchExecutionStrategy Strategy { get; init; }

    /// <summary>Vector score precision source.</summary>
    public required VectorScoreProvenance ScoreProvenance { get; init; }

    /// <summary>Whether candidate generation was exact.</summary>
    public required bool ExactCandidateSet { get; init; }

    /// <summary>Configured candidate bound where one exists.</summary>
    public int? CandidateLimit { get; init; }

    /// <summary>Total HNSW layer-zero node visits, including retry traversals.</summary>
    public required int HnswNodesVisited { get; init; }

    /// <summary>Total HNSW post-filter retry traversals.</summary>
    public required int HnswRetryCount { get; init; }

    /// <summary>Whether an HNSW node-visit budget stopped candidate generation.</summary>
    public required bool HnswBudgetExhausted { get; init; }

    /// <summary>
    /// Largest persisted reconstruction-error bound among returned approximate
    /// vector scores. A null value means no bound applies to the result.
    /// </summary>
    public float? MaximumScoreErrorBound { get; init; }

    /// <summary>Number of returned documents.</summary>
    public required int ReturnedCount { get; init; }

    /// <summary>Whether more collected hits existed than could be returned.</summary>
    public required bool Truncated { get; init; }

    /// <summary>Total elapsed search time.</summary>
    public required TimeSpan Elapsed { get; init; }
}

/// <summary>Search results paired with explicit execution diagnostics.</summary>
public sealed record SearchExecutionResult(
    TopDocs Results,
    SearchExecutionDiagnostics Diagnostics);

using Rowles.LeanLucene.Util;

namespace Rowles.LeanLucene.Search;

/// <summary>
/// Search-time options for HNSW graph traversal.
/// </summary>
public sealed class HnswSearchOptions
{
    /// <summary>Candidate set size (ef) maintained during traversal. Higher gives better recall.</summary>
    public int Ef { get; init; } = 10;

    /// <summary>
    /// Optional pre-filter: only documents whose identifier is contained in this set
    /// are visited during traversal. Used when the filter is highly selective.
    /// </summary>
    internal IBitSet? AllowList { get; init; }

    /// <summary>
    /// Optional post-filter: traversal is unrestricted, but candidates not contained
    /// in this set are dropped before returning. Used when the filter is loose.
    /// </summary>
    internal IBitSet? PostFilterMask { get; init; }

    /// <summary>Maximum results to return after filtering. Zero means unlimited.</summary>
    public int TopK { get; init; }

    /// <summary>
    /// Number of times <c>ef</c> is doubled when post-filtering leaves fewer than
    /// <see cref="TopK"/> survivors. Default is three.
    /// </summary>
    public int MaxPostFilterRetries { get; init; } = 3;
}

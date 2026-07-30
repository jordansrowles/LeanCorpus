using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Search;

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

    /// <summary>
    /// Maximum distinct layer-zero nodes visited by one traversal, including retries.
    /// Zero means unbounded. A bounded traversal returns its best available candidates
    /// and reports exhaustion through search diagnostics.
    /// </summary>
    public int MaxVisitedNodes { get; init; }

    /// <summary>
    /// Optional segment-local candidate document identifiers used as additional
    /// layer-zero entry points. This is reserved for deterministic lexical and
    /// learned-sparse planners.
    /// </summary>
    internal IReadOnlyList<int>? EntryPoints { get; init; }

    /// <summary>
    /// Bounded number of second-hop neighbours explored from rejected filter bridge
    /// nodes. Zero disables the ACORN-style expansion.
    /// </summary>
    internal int MaxFilterExpansion { get; init; }

    internal HnswTraversalOptions ToTraversalOptions() => new()
    {
        Ef = Ef,
        AllowList = AllowList,
        PostFilterMask = PostFilterMask,
        TopK = TopK,
        MaxPostFilterRetries = MaxPostFilterRetries,
        MaxVisitedNodes = MaxVisitedNodes,
        EntryPoints = EntryPoints,
        MaxFilterExpansion = MaxFilterExpansion,
    };
}

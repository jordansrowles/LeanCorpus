using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Search;

/// <summary>
/// Search-time options for HNSW graph traversal.
/// </summary>
public sealed class HnswSearchOptions
{
    /// <summary>Initialises HNSW options and captures the current process-wide search defaults.</summary>
    public HnswSearchOptions()
        : this(LeanCorpusDefaults.GetSnapshot())
    {
    }

    internal HnswSearchOptions(LeanCorpusDefaultSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Ef = snapshot.Search.Hnsw.Ef.IsSet ? snapshot.Search.Hnsw.Ef.Value : 10;
        MaxPostFilterRetries = snapshot.Search.Hnsw.MaxPostFilterRetries.IsSet
            ? snapshot.Search.Hnsw.MaxPostFilterRetries.Value
            : 3;
    }

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

    internal HnswTraversalOptions ToTraversalOptions() => new()
    {
        Ef = Ef,
        AllowList = AllowList,
        PostFilterMask = PostFilterMask,
        TopK = TopK,
        MaxPostFilterRetries = MaxPostFilterRetries,
    };
}

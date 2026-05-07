namespace Rowles.LeanLucene.Search.Queries;

/// <summary>
/// Configuration for MoreLikeThis term extraction and query generation.
/// </summary>
public sealed record MoreLikeThisParameters
{
    /// <summary>Minimum term frequency in the source document. Default: 1.</summary>
    public int MinTermFreq { get; init; } = 1;

    /// <summary>Minimum document frequency across the index. Default: 1.</summary>
    public int MinDocFreq { get; init; } = 1;

    /// <summary>Maximum document frequency (filters ultra-common terms). Default: int.MaxValue.</summary>
    public int MaxDocFreq { get; init; } = int.MaxValue;

    /// <summary>Maximum number of terms used in the generated query. Default: 25.</summary>
    public int MaxQueryTerms { get; init; } = 25;

    /// <summary>Minimum word length to consider. Default: 3.</summary>
    public int MinWordLength { get; init; } = 3;

    /// <summary>Whether to boost terms by their TF-IDF weight. Default: true.</summary>
    public bool BoostByScore { get; init; } = true;
}

using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Reranks a first-pass result set using one or more sort fields.</summary>
public sealed class SortRescorer
{
    private readonly SortField[] _sorts;

    /// <summary>Initialises a sort rescorer.</summary>
    public SortRescorer(params SortField[] sorts)
    {
        ArgumentNullException.ThrowIfNull(sorts);
        if (sorts.Length == 0)
            throw new ArgumentException("At least one sort field is required.", nameof(sorts));
        _sorts = sorts.ToArray();
    }

    /// <summary>Sorts and returns up to <paramref name="topN"/> first-pass documents.</summary>
    public TopDocs Rescore(IndexSearcher searcher, TopDocs firstPass, int topN)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        ArgumentNullException.ThrowIfNull(firstPass);
        if (topN <= 0 || firstPass.ScoreDocs.Length == 0)
            return TopDocs.Empty;

        var sorted = searcher.SortCandidates(firstPass.ScoreDocs, _sorts, topN);
        return new TopDocs(firstPass.TotalHits, sorted, firstPass.IsPartial);
    }
}

using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Search.Suggestions;

/// <summary>Provides analysed prefix suggestions with an optional context filter.</summary>
public static class AnalysingSuggester
{
    /// <summary>Returns analysed prefix completions ranked by matching document frequency.</summary>
    public static IReadOnlyList<(string Term, int DocFreq)> Suggest(
        IndexSearcher searcher,
        string input,
        string field,
        IAnalyser analyser,
        int topN = 5,
        Query? contextFilter = null)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        return searcher.Suggest(input, field, topN, analyser, contextFilter);
    }
}

using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Search.Suggestions;

/// <summary>Suggests terms that complete analysed free text in phrase context.</summary>
public static class FreeTextSuggester
{
    /// <summary>Returns phrase-context completions for the supplied input.</summary>
    public static IReadOnlyList<(string Term, int DocFreq)> Suggest(
        IndexSearcher searcher,
        string input,
        string field,
        IAnalyser analyser,
        int topN = 5)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        return searcher.SuggestNext(input, field, topN, analyser);
    }
}

using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Reranks a first-pass result set with scores from a second query.</summary>
public sealed class QueryRescorer
{
    private readonly Query _query;
    private readonly float _weight;

    /// <summary>Initialises a query rescorer.</summary>
    public QueryRescorer(Query query, float weight = 1.0f)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _weight = weight;
    }

    /// <summary>Rescores and returns up to <paramref name="topN"/> first-pass documents.</summary>
    public TopDocs Rescore(IndexSearcher searcher, TopDocs firstPass, int topN)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        ArgumentNullException.ThrowIfNull(firstPass);
        if (topN <= 0 || firstPass.ScoreDocs.Length == 0)
            return TopDocs.Empty;

        var secondPass = searcher.Search(_query, int.MaxValue);
        var secondScores = new Dictionary<int, float>(secondPass.ScoreDocs.Length);
        foreach (var scoreDoc in secondPass.ScoreDocs)
            secondScores[scoreDoc.DocId] = scoreDoc.Score;

        var rescored = firstPass.ScoreDocs.ToArray();
        for (int i = 0; i < rescored.Length; i++)
        {
            var scoreDoc = rescored[i];
            float secondScore = secondScores.GetValueOrDefault(scoreDoc.DocId);
            rescored[i] = new ScoreDoc(scoreDoc.DocId, scoreDoc.Score + _weight * secondScore);
        }

        Array.Sort(rescored, static (left, right) =>
        {
            int scoreComparison = right.Score.CompareTo(left.Score);
            return scoreComparison != 0
                ? scoreComparison
                : left.DocId.CompareTo(right.DocId);
        });

        if (rescored.Length > topN)
            Array.Resize(ref rescored, topN);
        return new TopDocs(firstPass.TotalHits, rescored, firstPass.IsPartial);
    }
}

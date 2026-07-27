using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Reranks a first-pass result set with scores from a second query.</summary>
public class QueryRescorer
{
    private readonly Query _query;
    private readonly float _firstPassWeight;
    private readonly float _secondPassWeight;

    /// <summary>Initialises a query rescorer.</summary>
    public QueryRescorer(Query query, float weight = 1.0f)
        : this(query, 1.0f, weight)
    {
    }

    /// <summary>Initialises a query rescorer with separate first-pass and second-pass weights.</summary>
    public QueryRescorer(Query query, float firstPassWeight, float secondPassWeight)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _firstPassWeight = firstPassWeight;
        _secondPassWeight = secondPassWeight;
    }

    /// <summary>Rescores and returns up to <paramref name="topN"/> first-pass documents.</summary>
    public TopDocs Rescore(IndexSearcher searcher, TopDocs firstPass, int topN)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        ArgumentNullException.ThrowIfNull(firstPass);
        if (topN <= 0 || firstPass.ScoreDocs.Length == 0)
            return TopDocs.Empty;

        var secondScores = new CandidateScoreCollectorStrategy(firstPass.ScoreDocs);
        _ = searcher.SearchWithCollectorStrategy(_query, secondScores);

        var rescored = firstPass.ScoreDocs.ToArray();
        for (int i = 0; i < rescored.Length; i++)
        {
            var scoreDoc = rescored[i];
            bool matched = secondScores.TryGetScore(scoreDoc.DocId, out float secondScore);
            rescored[i] = new ScoreDoc(
                scoreDoc.DocId,
                Combine(scoreDoc.Score, matched, secondScore));
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

    /// <summary>Combines the first-pass and optional second-pass scores.</summary>
    protected virtual float Combine(float firstPassScore, bool secondPassMatches, float secondPassScore)
        => (_firstPassWeight * firstPassScore)
            + (secondPassMatches ? _secondPassWeight * secondPassScore : 0.0f);

    private sealed class CandidateScoreCollectorStrategy : ITopNCollectorStrategy
    {
        private readonly Candidate[] _candidates;
        private readonly float[] _scores;
        private readonly bool[] _matches;
        private int _totalHits;

        internal CandidateScoreCollectorStrategy(ScoreDoc[] candidates)
        {
            _candidates = new Candidate[candidates.Length];
            _scores = new float[candidates.Length];
            _matches = new bool[candidates.Length];
            for (int i = 0; i < candidates.Length; i++)
                _candidates[i] = new Candidate(candidates[i].DocId, i);
            Array.Sort(
                _candidates,
                static (left, right) => left.DocId.CompareTo(right.DocId));
        }

        public int TotalHits => _totalHits;
        public int Capacity => _candidates.Length;
        public bool IsFull => false;
        public float MinScore => float.NegativeInfinity;

        public void Collect(int docId, float score)
        {
            _totalHits++;
            int index = FindCandidate(docId);
            if (index < 0)
                return;

            int originalIndex = _candidates[index].OriginalIndex;
            _scores[originalIndex] = score;
            _matches[originalIndex] = true;
        }

        public TopDocs ToTopDocs() => TopDocs.Empty;

        public void Reset()
        {
            _totalHits = 0;
            Array.Clear(_scores);
            Array.Clear(_matches);
        }

        internal bool TryGetScore(int docId, out float score)
        {
            int index = FindCandidate(docId);
            if (index >= 0)
            {
                int originalIndex = _candidates[index].OriginalIndex;
                if (_matches[originalIndex])
                {
                    score = _scores[originalIndex];
                    return true;
                }
            }

            score = 0;
            return false;
        }

        private int FindCandidate(int docId)
        {
            int low = 0;
            int high = _candidates.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int comparison = _candidates[middle].DocId.CompareTo(docId);
                if (comparison == 0)
                    return middle;
                if (comparison < 0)
                    low = middle + 1;
                else
                    high = middle - 1;
            }
            return -1;
        }

        private readonly record struct Candidate(int DocId, int OriginalIndex);
    }
}

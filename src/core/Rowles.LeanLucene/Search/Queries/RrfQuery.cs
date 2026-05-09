namespace Rowles.LeanLucene.Search.Queries;

/// <summary>
/// Reciprocal Rank Fusion (RRF) query that merges result lists from multiple child
/// queries without requiring score normalisation.
/// <para>
/// Score formula: <c>score(d) = Σ 1/(k + rank_i(d))</c> where <c>k</c> defaults to 60.
/// </para>
/// </summary>
public sealed class RrfQuery : Query
{
    private readonly List<Query> _queries = [];

    /// <summary>The ranking constant <c>k</c>. Higher values reduce the impact of top-ranked results. Default: 60.</summary>
    public int K { get; }

    /// <summary>The child queries whose result lists will be fused.</summary>
    public IReadOnlyList<Query> Queries => _queries;

    /// <inheritdoc/>
    public override string Field => _queries.Count > 0 ? _queries[0].Field : string.Empty;

    /// <summary>Initialises a new <see cref="RrfQuery"/> with the given rank constant.</summary>
    /// <param name="k">
    /// The ranking constant. Higher values reduce the impact of top-ranked results.
    /// Must be greater than zero. Default: 60.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="k"/> is zero or negative.</exception>
    public RrfQuery(int k = 60)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        K = k;
    }

    /// <summary>Adds a child query whose results will be fused. Returns <c>this</c> for chaining.</summary>
    public RrfQuery Add(Query query)
    {
        ArgumentNullException.ThrowIfNull(query);
        _queries.Add(query);
        return this;
    }

    /// <summary>
    /// Combines multiple <see cref="Scoring.TopDocs"/> result sets using RRF scoring.
    /// </summary>
    public static Scoring.TopDocs Combine(Scoring.TopDocs[] resultSets, int topN, int k = 60)
    {
        if (resultSets.Length == 0 || topN <= 0)
            return Scoring.TopDocs.Empty;

        // docId → accumulated RRF score
        var scores = new Dictionary<int, float>();

        foreach (var results in resultSets)
        {
            for (int rank = 0; rank < results.ScoreDocs.Length; rank++)
            {
                int docId = results.ScoreDocs[rank].DocId;
                float rrfScore = 1.0f / (k + rank + 1); // rank is 0-based, formula uses 1-based
                scores[docId] = scores.GetValueOrDefault(docId) + rrfScore;
            }
        }

        // Sort by RRF score descending, then take topN
        var sorted = new List<Scoring.ScoreDoc>(scores.Count);
        foreach (var (docId, score) in scores)
            sorted.Add(new Scoring.ScoreDoc(docId, score));
        sorted.Sort((a, b) => b.Score.CompareTo(a.Score));

        if (sorted.Count > topN)
            sorted.RemoveRange(topN, sorted.Count - topN);

        return new Scoring.TopDocs(scores.Count, sorted.ToArray());
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is RrfQuery other &&
        K == other.K && Boost == other.Boost &&
        _queries.Count == other._queries.Count &&
        _queries.SequenceEqual(other._queries);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(nameof(RrfQuery));
        h.Add(K);
        foreach (var q in _queries) h.Add(q);
        return CombineBoost(h.ToHashCode());
    }
}

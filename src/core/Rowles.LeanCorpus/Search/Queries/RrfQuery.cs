namespace Rowles.LeanCorpus.Search.Queries;

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
    private readonly List<RrfChild> _children = [];

    /// <summary>The ranking constant <c>k</c>. Higher values reduce the impact of top-ranked results. Default: 60.</summary>
    public int K { get; }

    /// <summary>The child queries whose result lists will be fused.</summary>
    public IReadOnlyList<Query> Queries => _queries;

    /// <summary>The child queries together with their independent candidate windows and weights.</summary>
    public IReadOnlyList<RrfChild> Children => _children;

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
        _children.Add(new RrfChild(query, CandidateWindow: 0, Weight: 1f));
        return this;
    }

    /// <summary>Adds a weighted child with an independent candidate window.</summary>
    /// <param name="query">Child query to execute.</param>
    /// <param name="candidateWindow">Number of ranked candidates to request from this child.</param>
    /// <param name="weight">Positive finite contribution multiplier.</param>
    public RrfQuery Add(Query query, int candidateWindow, float weight = 1f)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(candidateWindow);
        if (!float.IsFinite(weight) || weight <= 0f)
            throw new ArgumentOutOfRangeException(
                nameof(weight),
                "RRF child weights must be positive and finite.");
        _queries.Add(query);
        _children.Add(new RrfChild(query, candidateWindow, weight));
        return this;
    }

    /// <summary>
    /// Combines multiple <see cref="Scoring.TopDocs"/> result sets using RRF scoring.
    /// </summary>
    public static Scoring.TopDocs Combine(Scoring.TopDocs[] resultSets, int topN, int k = 60)
        => CombineCore(resultSets, weights: null, topN, k);

    /// <summary>Combines ranked result sets using one positive finite weight per child.</summary>
    public static Scoring.TopDocs Combine(
        Scoring.TopDocs[] resultSets,
        IReadOnlyList<float> weights,
        int topN,
        int k = 60)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count != resultSets.Length)
            throw new ArgumentException(
                "RRF weights must contain exactly one value per result set.",
                nameof(weights));
        for (int i = 0; i < weights.Count; i++)
        {
            if (!float.IsFinite(weights[i]) || weights[i] <= 0f)
                throw new ArgumentOutOfRangeException(
                    nameof(weights),
                    "RRF weights must be positive and finite.");
        }
        return CombineCore(resultSets, weights, topN, k);
    }

    private static Scoring.TopDocs CombineCore(
        Scoring.TopDocs[] resultSets,
        IReadOnlyList<float>? weights,
        int topN,
        int k)
    {
        if (resultSets.Length == 0 || topN <= 0)
            return Scoring.TopDocs.Empty;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);

        // docId → accumulated RRF score
        var scores = new Dictionary<int, float>();
        var bestRanks = new Dictionary<int, int>();

        for (int childIndex = 0; childIndex < resultSets.Length; childIndex++)
        {
            var results = resultSets[childIndex];
            float weight = weights?[childIndex] ?? 1f;
            for (int rank = 0; rank < results.ScoreDocs.Length; rank++)
            {
                int docId = results.ScoreDocs[rank].DocId;
                int oneBasedRank = rank + 1;
                float rrfScore = weight / (k + oneBasedRank);
                scores[docId] = scores.GetValueOrDefault(docId) + rrfScore;
                bestRanks[docId] = Math.Min(
                    bestRanks.GetValueOrDefault(docId, int.MaxValue),
                    oneBasedRank);
            }
        }

        // Stable ordering: score descending, best child rank ascending, then doc ID.
        var sorted = new List<Scoring.ScoreDoc>(scores.Count);
        foreach (var (docId, score) in scores)
            sorted.Add(new Scoring.ScoreDoc(docId, score));
        sorted.Sort((a, b) =>
        {
            int comparison = b.Score.CompareTo(a.Score);
            if (comparison != 0)
                return comparison;
            comparison = bestRanks[a.DocId].CompareTo(bestRanks[b.DocId]);
            return comparison != 0 ? comparison : a.DocId.CompareTo(b.DocId);
        });

        if (sorted.Count > topN)
            sorted.RemoveRange(topN, sorted.Count - topN);

        return new Scoring.TopDocs(scores.Count, sorted.ToArray());
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is RrfQuery other &&
        K == other.K && Boost == other.Boost &&
        _children.SequenceEqual(other._children);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(nameof(RrfQuery));
        h.Add(K);
        foreach (var child in _children) h.Add(child);
        return CombineBoost(h.ToHashCode());
    }

    /// <inheritdoc/>
    public override void Visit(QueryVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var query in _queries)
            query.Visit(visitor.GetSubVisitor(Occur.Should, this));
    }

    /// <summary>Configuration for one RRF child query.</summary>
    public sealed record RrfChild(Query Query, int CandidateWindow, float Weight);
}

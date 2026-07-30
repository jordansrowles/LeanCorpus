namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Supported bounded score-fusion methods.</summary>
public enum FusionMethod
{
    /// <summary>Weighted reciprocal-rank fusion.</summary>
    WeightedRrf,

    /// <summary>Per-child min-max normalisation followed by a weighted sum.</summary>
    NormalisedLinear,

    /// <summary>Weighted sum of log odds for child scores calibrated to probabilities.</summary>
    LogOdds,
}

/// <summary>
/// Combines independently windowed child rankings using a declared calibration method.
/// </summary>
public sealed class FusionQuery : Query
{
    private readonly List<FusionChild> _children = [];

    private int _sparseSeedCandidateLimit;

    /// <summary>Fusion method.</summary>
    public FusionMethod Method { get; }

    /// <summary>RRF rank constant, used only by <see cref="FusionMethod.WeightedRrf"/>.</summary>
    public int RankConstant { get; }

    /// <summary>Configured child queries.</summary>
    public IReadOnlyList<FusionChild> Children => _children;

    /// <summary>
    /// Gets the maximum number of learned-sparse candidates that may be supplied as
    /// additional HNSW entry points to dense child queries. Zero disables automatic
    /// sparse-to-dense seeding.
    /// </summary>
    public int SparseSeedCandidateLimit => _sparseSeedCandidateLimit;

    /// <inheritdoc/>
    public override string Field => _children.Count > 0 ? _children[0].Query.Field : string.Empty;

    /// <summary>Initialises a bounded fusion query.</summary>
    public FusionQuery(FusionMethod method = FusionMethod.WeightedRrf, int rankConstant = 60)
    {
        if (!Enum.IsDefined(method))
            throw new ArgumentOutOfRangeException(nameof(method));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rankConstant);
        Method = method;
        RankConstant = rankConstant;
    }

    /// <summary>Adds a positive-weight child with an independent positive candidate window.</summary>
    public FusionQuery Add(Query query, int candidateWindow, float weight = 1f)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(candidateWindow);
        if (!float.IsFinite(weight) || weight <= 0f)
            throw new ArgumentOutOfRangeException(
                nameof(weight),
                "Fusion child weights must be positive and finite.");
        _children.Add(new FusionChild(query, candidateWindow, weight));
        return this;
    }

    /// <summary>
    /// Enables bounded learned-sparse-to-dense seeding for this fusion query.
    /// </summary>
    /// <remarks>
    /// Only candidates returned by direct <see cref="SparseImpactQuery"/> children
    /// are considered. The candidate limit is global to the fusion execution and
    /// remains independent of each child's bounded result window.
    /// </remarks>
    /// <param name="candidateLimit">Maximum distinct sparse candidates used as HNSW seeds.</param>
    public FusionQuery UseSparseVectorSeeds(int candidateLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(candidateLimit);
        _sparseSeedCandidateLimit = candidateLimit;
        return this;
    }

    /// <summary>Combines already-ranked child result sets.</summary>
    public static Scoring.TopDocs Combine(
        Scoring.TopDocs[] resultSets,
        IReadOnlyList<float> weights,
        int topN,
        FusionMethod method,
        int rankConstant = 60)
    {
        ArgumentNullException.ThrowIfNull(resultSets);
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count != resultSets.Length)
            throw new ArgumentException(
                "Fusion weights must contain exactly one value per result set.",
                nameof(weights));
        if (!Enum.IsDefined(method))
            throw new ArgumentOutOfRangeException(nameof(method));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rankConstant);
        for (int i = 0; i < weights.Count; i++)
        {
            if (!float.IsFinite(weights[i]) || weights[i] <= 0f)
                throw new ArgumentOutOfRangeException(
                    nameof(weights),
                    "Fusion weights must be positive and finite.");
        }
        if (resultSets.Length == 0 || topN <= 0)
            return Scoring.TopDocs.Empty;

        var scores = new Dictionary<int, double>();
        var bestRanks = new Dictionary<int, int>();
        for (int childIndex = 0; childIndex < resultSets.Length; childIndex++)
        {
            var hits = resultSets[childIndex].ScoreDocs;
            float weight = weights[childIndex];
            float min = 0f;
            float max = 0f;
            if (method == FusionMethod.NormalisedLinear && hits.Length > 0)
            {
                min = hits.Min(hit => hit.Score);
                max = hits.Max(hit => hit.Score);
                if (!float.IsFinite(min) || !float.IsFinite(max))
                    throw new InvalidDataException("Fusion children must produce finite scores.");
            }

            var seen = new HashSet<int>();
            for (int rank = 0; rank < hits.Length; rank++)
            {
                var hit = hits[rank];
                if (!seen.Add(hit.DocId))
                    continue;
                if (!float.IsFinite(hit.Score))
                    throw new InvalidDataException("Fusion children must produce finite scores.");

                double contribution = method switch
                {
                    FusionMethod.WeightedRrf =>
                        weight / (rankConstant + rank + 1d),
                    FusionMethod.NormalisedLinear =>
                        weight * (max > min ? (hit.Score - min) / (max - min) : 1d),
                    FusionMethod.LogOdds =>
                        weight * CalibratedLogOdds(hit.Score),
                    _ => throw new ArgumentOutOfRangeException(nameof(method)),
                };
                scores[hit.DocId] = scores.GetValueOrDefault(hit.DocId) + contribution;
                bestRanks[hit.DocId] = Math.Min(
                    bestRanks.GetValueOrDefault(hit.DocId, int.MaxValue),
                    rank + 1);
            }
        }

        var combined = scores
            .Select(pair => new Scoring.ScoreDoc(pair.Key, (float)pair.Value))
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => bestRanks[hit.DocId])
            .ThenBy(hit => hit.DocId)
            .Take(topN)
            .ToArray();
        return new Scoring.TopDocs(scores.Count, combined);
    }

    private static double CalibratedLogOdds(float score)
    {
        if (score is < 0f or > 1f)
            throw new InvalidDataException(
                "Log-odds fusion requires child scores calibrated to the inclusive range 0..1.");
        const double epsilon = 1e-6;
        double probability = Math.Clamp(score, epsilon, 1d - epsilon);
        return Math.Log(probability / (1d - probability));
    }

    /// <inheritdoc/>
    public override void Visit(QueryVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var child in _children)
            child.Query.Visit(visitor.GetSubVisitor(Occur.Should, this));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is FusionQuery other &&
        Method == other.Method &&
        RankConstant == other.RankConstant &&
        SparseSeedCandidateLimit == other.SparseSeedCandidateLimit &&
        Boost == other.Boost &&
        _children.SequenceEqual(other._children);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(FusionQuery));
        hash.Add(Method);
        hash.Add(RankConstant);
        hash.Add(SparseSeedCandidateLimit);
        foreach (var child in _children)
            hash.Add(child);
        return CombineBoost(hash.ToHashCode());
    }

    /// <summary>Configuration for one fusion child.</summary>
    public sealed record FusionChild(Query Query, int CandidateWindow, float Weight);
}

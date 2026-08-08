using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches every live document and scores it from a <see cref="DoubleValuesSource"/>.</summary>
public sealed class FunctionQuery : Query
{
    /// <summary>Gets the source used to produce document scores.</summary>
    public DoubleValuesSource ValuesSource { get; }

    /// <inheritdoc/>
    public override string Field => string.Empty;

    /// <summary>Initialises a function query.</summary>
    public FunctionQuery(DoubleValuesSource valuesSource)
    {
        ValuesSource = valuesSource ?? throw new ArgumentNullException(nameof(valuesSource));
    }

    /// <inheritdoc/>
    public override Weight CreateWeight(IndexSearcher searcher)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        return new FunctionWeight(ValuesSource.Rewrite(searcher), Boost);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is FunctionQuery other
            && ValuesSource.Equals(other.ValuesSource)
            && Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode()
        => CombineBoost(HashCode.Combine(nameof(FunctionQuery), ValuesSource));

    private sealed class FunctionWeight : Weight
    {
        private readonly DoubleValuesSource _valuesSource;
        private readonly float _boost;

        internal FunctionWeight(DoubleValuesSource valuesSource, float boost)
            : base(new MatchAllDocsQuery())
        {
            _valuesSource = valuesSource;
            _boost = boost;
        }

        public override Scorer CreateScorer(IndexSearcher searcher)
            => new FunctionScorer(searcher, _valuesSource, _boost);
    }

    private sealed class FunctionScorer : Scorer
    {
        private readonly IndexSearcher _searcher;
        private readonly DoubleValuesSource _valuesSource;
        private readonly float _boost;

        internal FunctionScorer(
            IndexSearcher searcher,
            DoubleValuesSource valuesSource,
            float boost)
        {
            _searcher = searcher;
            _valuesSource = valuesSource;
            _boost = boost;
        }

        public override float Score(int docId, float approximationScore)
            => _valuesSource.TryGetValue(
                _searcher,
                docId,
                approximationScore,
                out double value)
                ? (float)value * _boost
                : 0.0f;
    }
}

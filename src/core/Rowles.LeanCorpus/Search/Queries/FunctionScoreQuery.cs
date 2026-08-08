using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>
/// Boosts an inner query's relevance score by a numeric field value.
/// </summary>
public sealed class FunctionScoreQuery : Query
{
    /// <summary>Gets the inner query whose score is used as the base for the function score.</summary>
    public Query Inner { get; }

    /// <summary>Gets the name of the numeric field whose value modifies the base score.</summary>
    public string NumericField { get; }

    /// <summary>Gets the source whose value modifies the base score.</summary>
    public DoubleValuesSource ValuesSource { get; }

    /// <summary>Gets the mode that controls how the numeric field value is combined with the query score.</summary>
    public ScoreMode Mode { get; }

    /// <inheritdoc/>
    public override string Field => Inner.Field;

    /// <summary>Initialises a new <see cref="FunctionScoreQuery"/> wrapping the given inner query.</summary>
    /// <param name="inner">The base query providing initial scores.</param>
    /// <param name="numericField">The numeric field whose stored value modifies the score.</param>
    /// <param name="mode">How to combine the base score with the numeric field value.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is <see langword="null"/>.</exception>
    public FunctionScoreQuery(Query inner, string numericField, ScoreMode mode = ScoreMode.Multiply)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (string.IsNullOrWhiteSpace(numericField))
            throw new ArgumentException("Numeric field must be a non-empty value.", nameof(numericField));
        Inner = inner;
        NumericField = numericField;
        ValuesSource = DoubleValuesSource.FromDoubleField(numericField);
        Mode = mode;
    }

    /// <summary>Initialises a function score query using a composed value source.</summary>
    public FunctionScoreQuery(
        Query inner,
        DoubleValuesSource valuesSource,
        ScoreMode mode = ScoreMode.Multiply)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ValuesSource = valuesSource ?? throw new ArgumentNullException(nameof(valuesSource));
        NumericField = string.Empty;
        Mode = mode;
    }

    internal bool IsSimpleNumericField => NumericField.Length != 0;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is FunctionScoreQuery other &&
        Inner.Equals(other.Inner) &&
        ValuesSource.Equals(other.ValuesSource) &&
        Mode == other.Mode && Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() =>
        CombineBoost(HashCode.Combine(nameof(FunctionScoreQuery), Inner, ValuesSource, Mode));

    /// <inheritdoc/>
    public override void Visit(QueryVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        Inner.Visit(visitor.GetSubVisitor(Occur.Must, this));
    }

    /// <summary>Combines a query score with a numeric field value according to the specified <see cref="ScoreMode"/>.</summary>
    /// <param name="queryScore">The BM25 score from the inner query.</param>
    /// <param name="fieldValue">The stored numeric field value.</param>
    /// <param name="mode">The combination mode.</param>
    /// <returns>The combined score.</returns>
    public static float Combine(float queryScore, double fieldValue, ScoreMode mode) => mode switch
    {
        ScoreMode.Multiply => queryScore * (float)fieldValue,
        ScoreMode.Replace => (float)fieldValue,
        ScoreMode.Sum => queryScore + (float)fieldValue,
        ScoreMode.Max => MathF.Max(queryScore, (float)fieldValue),
        _ => queryScore
    };
}

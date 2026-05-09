namespace Rowles.LeanLucene.Search.Queries;

/// <summary>
/// Matches documents whose field value falls within a lexicographic range.
/// Bounds can be inclusive or exclusive; <c>null</c> means unbounded.
/// </summary>
public sealed class TermRangeQuery : Query
{
    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the optional inclusive or exclusive lower bound, or <see langword="null"/> for unbounded.</summary>
    public string? LowerTerm { get; }

    /// <summary>Gets the optional inclusive or exclusive upper bound, or <see langword="null"/> for unbounded.</summary>
    public string? UpperTerm { get; }

    /// <summary>Gets a value indicating whether the lower bound is inclusive.</summary>
    public bool IncludeLower { get; }

    /// <summary>Gets a value indicating whether the upper bound is inclusive.</summary>
    public bool IncludeUpper { get; }

    /// <summary>Initialises a new <see cref="TermRangeQuery"/> for the given field and lexicographic bounds.</summary>
    /// <param name="field">The field to search.</param>
    /// <param name="lowerTerm">The lower bound term, or <see langword="null"/> for no lower bound.</param>
    /// <param name="upperTerm">The upper bound term, or <see langword="null"/> for no upper bound.</param>
    /// <param name="includeLower">When <see langword="true"/>, include the lower bound in the range.</param>
    /// <param name="includeUpper">When <see langword="true"/>, include the upper bound in the range.</param>
    public TermRangeQuery(string field, string? lowerTerm, string? upperTerm,
        bool includeLower = true, bool includeUpper = true)
    {
        Field = field;
        LowerTerm = lowerTerm;
        UpperTerm = upperTerm;
        IncludeLower = includeLower;
        IncludeUpper = includeUpper;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is TermRangeQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        string.Equals(LowerTerm, other.LowerTerm, StringComparison.Ordinal) &&
        string.Equals(UpperTerm, other.UpperTerm, StringComparison.Ordinal) &&
        IncludeLower == other.IncludeLower &&
        IncludeUpper == other.IncludeUpper &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() =>
        CombineBoost(HashCode.Combine(nameof(TermRangeQuery), Field, LowerTerm, UpperTerm, IncludeLower, IncludeUpper));
}

namespace Rowles.LeanLucene.Search.Queries;

/// <summary>A span query matching a single term.</summary>
public sealed class SpanTermQuery : SpanQuery
{
    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the term that this span query matches.</summary>
    public string Term { get; }

    /// <summary>Cached qualified term to avoid repeated string.Concat.</summary>
    internal string? CachedQualifiedTerm { get; set; }

    /// <summary>Initialises a new <see cref="SpanTermQuery"/> for the given field and term.</summary>
    /// <param name="field">The field to search.</param>
    /// <param name="term">The exact term to match as a span.</param>
    public SpanTermQuery(string field, string term)
    {
        Field = field;
        Term = term;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is SpanTermQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        string.Equals(Term, other.Term, StringComparison.Ordinal) &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(HashCode.Combine(nameof(SpanTermQuery), Field, Term));
}

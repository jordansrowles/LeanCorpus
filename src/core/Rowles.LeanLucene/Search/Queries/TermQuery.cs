namespace Rowles.LeanLucene.Search.Queries;

/// <summary>
/// Single exact-term lookup against the index.
/// </summary>
public sealed class TermQuery : Query
{
    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the exact term to match against indexed tokens.</summary>
    public string Term { get; }

    /// <summary>Lazily-cached "field\0term" string used by IndexSearcher to avoid per-search allocation.</summary>
    internal string? CachedQualifiedTerm { get; set; }

    /// <summary>Initialises a new <see cref="TermQuery"/> for the given field and term.</summary>
    /// <param name="field">The field to search.</param>
    /// <param name="term">The exact term to match.</param>
    public TermQuery(string field, string term)
    {
        Field = field;
        Term = term;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is TermQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        string.Equals(Term, other.Term, StringComparison.Ordinal) &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(HashCode.Combine(nameof(TermQuery), Field, Term));
}

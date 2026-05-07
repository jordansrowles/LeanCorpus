namespace Rowles.LeanLucene.Search.Queries;

/// <summary>
/// Matches terms within a Levenshtein edit distance of the target term.
/// </summary>
public sealed class FuzzyQuery : Query
{
    /// <summary>Default maximum number of matching terms to expand.</summary>
    public const int DefaultMaxExpansions = 64;

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the target term that candidate terms are compared against.</summary>
    public string Term { get; }

    /// <summary>Gets the maximum number of Levenshtein edits allowed for a term to match.</summary>
    public int MaxEdits { get; }

    /// <summary>
    /// Maximum number of matching terms to expand into the scored query.
    /// When more terms match, only the closest (lowest edit distance) are kept.
    /// </summary>
    public int MaxExpansions { get; }

    /// <summary>Initialises a new <see cref="FuzzyQuery"/> for the given field and term.</summary>
    /// <param name="field">The field to search.</param>
    /// <param name="term">The target term to fuzzily match against.</param>
    /// <param name="maxEdits">Maximum Levenshtein edit distance (0–2). Default: 2.</param>
    /// <param name="maxExpansions">Maximum number of expanded terms to score. Default: <see cref="DefaultMaxExpansions"/>.</param>
    public FuzzyQuery(string field, string term, int maxEdits = 2, int maxExpansions = DefaultMaxExpansions)
    {
        Field = field;
        Term = term;
        MaxEdits = maxEdits;
        MaxExpansions = maxExpansions;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is FuzzyQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        string.Equals(Term, other.Term, StringComparison.Ordinal) &&
        MaxEdits == other.MaxEdits && MaxExpansions == other.MaxExpansions && Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(HashCode.Combine(nameof(FuzzyQuery), Field, Term, MaxEdits, MaxExpansions));
}

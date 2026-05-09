namespace Rowles.LeanLucene.Search.Queries;

/// <summary>
/// Matches all terms starting with a given prefix.
/// </summary>
public sealed class PrefixQuery : Query
{
    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the prefix that all matched terms must start with.</summary>
    public string Prefix { get; }

    /// <summary>Initialises a new <see cref="PrefixQuery"/> for the given field and prefix.</summary>
    /// <param name="field">The field to search.</param>
    /// <param name="prefix">The prefix string that matching terms must begin with.</param>
    public PrefixQuery(string field, string prefix)
    {
        Field = field;
        Prefix = prefix;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is PrefixQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        string.Equals(Prefix, other.Prefix, StringComparison.Ordinal) &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(HashCode.Combine(nameof(PrefixQuery), Field, Prefix));
}

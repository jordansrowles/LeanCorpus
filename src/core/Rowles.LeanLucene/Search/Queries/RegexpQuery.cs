using System.Text.RegularExpressions;

namespace Rowles.LeanLucene.Search.Queries;

/// <summary>
/// Matches all documents that contain a term matching the provided regular expression.
/// The regex is applied to the bare term text (not the qualified field\0term form).
/// </summary>
public sealed class RegexpQuery : Query
{
    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the regular expression pattern applied to bare term text.</summary>
    public string Pattern { get; }

    internal Regex CompiledRegex { get; }

    /// <summary>Initialises a new <see cref="RegexpQuery"/> for the given field and pattern.</summary>
    /// <param name="field">The field to search.</param>
    /// <param name="pattern">The regular expression pattern to match against term text.</param>
    /// <param name="options">Additional <see cref="RegexOptions"/> flags. <see cref="RegexOptions.Compiled"/> is ignored for Native AOT compatibility.</param>
    public RegexpQuery(string field, string pattern, RegexOptions options = RegexOptions.None)
    {
        Field = field;
        Pattern = pattern;
        CompiledRegex = new Regex(pattern, options & ~RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is RegexpQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        string.Equals(Pattern, other.Pattern, StringComparison.Ordinal) &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(HashCode.Combine(nameof(RegexpQuery), Field, Pattern));
}

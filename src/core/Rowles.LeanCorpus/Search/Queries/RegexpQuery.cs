using System.Text.RegularExpressions;

namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>
/// Matches all documents that contain a term matching the provided regular expression.
/// The expression must match the whole bare term, like Lucene's <c>RegexpQuery</c> (ADR048):
/// <c>gov.*ment</c> and <c>^gov.*ment$</c> are the same query, and <c>ment</c> does not match
/// <c>government</c>. Use explicit wildcards, such as <c>.*ment.*</c>, to match within a term.
/// </summary>
public sealed class RegexpQuery : Query
{
    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the regular expression pattern, which must match the whole bare term text.</summary>
    public string Pattern { get; }

    internal Regex CompiledRegex { get; }

    /// <summary>Initialises a new <see cref="RegexpQuery"/> for the given field and pattern.</summary>
    /// <param name="field">The field to search.</param>
    /// <param name="pattern">The regular expression pattern to match against term text.</param>
    /// <param name="options">Additional <see cref="RegexOptions"/> flags. <see cref="RegexOptions.Compiled"/> is ignored for Native AOT compatibility.</param>
    public RegexpQuery(string field, string pattern, RegexOptions options = RegexOptions.None)
        : this(field, pattern, options, null)
    {
    }

    /// <summary>Initialises a regular expression with an optional distinct matched-term expansion limit.</summary>
    /// <param name="field">The field to search.</param>
    /// <param name="pattern">The regular expression pattern.</param>
    /// <param name="maximumExpansions">A positive per-clause limit, or null for trusted unbounded execution.</param>
    /// <param name="options">Regular expression flags, excluding compiled execution for Native AOT.</param>
    public RegexpQuery(string field, string pattern, RegexOptions options, int? maximumExpansions)
    {
        if (maximumExpansions is <= 0) throw new ArgumentOutOfRangeException(nameof(maximumExpansions));
        MaximumExpansions = maximumExpansions;
        Field = field;
        Pattern = pattern;
        var regexOptions = options & ~RegexOptions.Compiled;
        // Compile the caller's pattern on its own first, so an unbalanced group such as "a)|(b"
        // is rejected rather than escaping the whole-term anchors below.
        _ = new Regex(pattern, regexOptions, TimeSpan.FromSeconds(1));
        CompiledRegex = new Regex(string.Concat(@"\A(?:", pattern, @")\z"), regexOptions, TimeSpan.FromSeconds(1));
    }

    /// <summary>Gets the distinct matched-term limit, or null for trusted unbounded execution.</summary>
    public int? MaximumExpansions { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is RegexpQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        string.Equals(Pattern, other.Pattern, StringComparison.Ordinal) &&
        MaximumExpansions == other.MaximumExpansions &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(HashCode.Combine(nameof(RegexpQuery), Field, Pattern, MaximumExpansions));
}

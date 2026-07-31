using System.Text.RegularExpressions;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Applies a regex replacement to each token's text.
/// </summary>
/// <remarks>
/// <para>This operates on individual tokens (unlike <see cref="PatternReplaceCharFilter"/>
/// which operates on the raw character input before tokenisation).</para>
/// <para>The regex uses the interpreter (not <see cref="RegexOptions.Compiled"/>) for
/// Native AOT compatibility and applies <see cref="RegexOptions.CultureInvariant"/> by default;
/// callers may override via the constructor's <c>options</c> parameter, which is merged with the defaults
/// via bitwise OR. Callers requiring compilation should use the
/// <see cref="PatternReplaceFilter(Regex, string)"/> constructor with a pre-compiled regex.</para>
/// <para>Tokens whose text does not match the pattern are forwarded unchanged with
/// a single zero-allocation <see cref="Regex.IsMatch(ReadOnlySpan{char})"/> check.</para>
/// </remarks>
public sealed class PatternReplaceFilter : ISpanTokenFilter
{
    private readonly Regex _regex;
    private readonly string _replacement;

    /// <summary>
    /// Initialises a new <see cref="PatternReplaceFilter"/> with the specified regex
    /// pattern and replacement string.
    /// </summary>
    /// <param name="pattern">A regular expression pattern to match against each token.</param>
    /// <param name="replacement">The replacement string. Supports regex substitution
    /// syntax (<c>$1</c>, <c>${name}</c>, etc.).</param>
    /// <param name="options">Optional regex options merged with the defaults
    /// (<see cref="RegexOptions.CultureInvariant"/>). <see cref="RegexOptions.Compiled"/>
    /// is not supported on Native AOT.</param>
    public PatternReplaceFilter(string pattern, string replacement, RegexOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(replacement);

        RegexOptions merged = (options ?? RegexOptions.None)
            | RegexOptions.CultureInvariant;

        _regex = new Regex(pattern, merged, TimeSpan.FromSeconds(1));
        _replacement = replacement;
    }

    /// <summary>
    /// Initialises a new <see cref="PatternReplaceFilter"/> with a pre-compiled
    /// <see cref="Regex"/> and replacement string. The caller retains full control
    /// over regex options and lifetime.
    /// </summary>
    /// <param name="regex">A pre-compiled regex instance.</param>
    /// <param name="replacement">The replacement string.</param>
    public PatternReplaceFilter(Regex regex, string replacement)
    {
        _regex = regex ?? throw new ArgumentNullException(nameof(regex));
        _replacement = replacement ?? throw new ArgumentNullException(nameof(replacement));
    }

    /// <inheritdoc/>
    public void Apply(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type,
        int positionIncrement,
        byte[]? payload,
        ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        // Fast path: if the pattern doesn't match, forward the span unchanged.
        // Regex.IsMatch(ReadOnlySpan<char>) is allocation-free (added in .NET 7).
        if (!_regex.IsMatch(text))
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            return;
        }

        // Match found — perform the replacement. The instance Regex.Replace only
        // accepts string, so we materialise the span. The allocation is unavoidable
        // when a match occurs because the replacement string must be allocated anyway.
        string replaced = _regex.Replace(text.ToString(), _replacement);
        sink.Add(replaced.AsSpan(), startOffset, endOffset, type, positionIncrement, payload);
    }
}

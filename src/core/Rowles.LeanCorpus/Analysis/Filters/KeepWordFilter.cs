using System.Collections.Frozen;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Removes any token whose text is not present in the configured keep-word set.
/// </summary>
public sealed class KeepWordFilter : ITokenFilter
{
    private readonly FrozenSet<string> _words;

    /// <summary>
    /// Initialises a new <see cref="KeepWordFilter"/>.
    /// </summary>
    /// <param name="words">Words to keep in the token stream.</param>
    public KeepWordFilter(IEnumerable<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        _words = words.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        tokens.RemoveAll(token => !_words.Contains(token.Text));
    }
}

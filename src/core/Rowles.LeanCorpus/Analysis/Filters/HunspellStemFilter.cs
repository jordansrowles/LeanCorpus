using Rowles.LeanCorpus.Analysis;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Stems tokens using a pre-parsed <see cref="HunspellDictionary"/>.
/// </summary>
public sealed class HunspellStemFilter : ISpanTokenFilter
{
    private readonly HunspellDictionary _dictionary;
    private readonly bool _injectAlternates;

    /// <summary>
    /// Initialises a new <see cref="HunspellStemFilter"/>.
    /// </summary>
    /// <param name="dictionary">The Hunspell dictionary to use.</param>
    /// <param name="injectAlternates">When true, keeps the original token and emits stems at the same position.</param>
    public HunspellStemFilter(HunspellDictionary dictionary, bool injectAlternates = false)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _injectAlternates = injectAlternates;
    }

    /// <inheritdoc/>

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
        var stems = _dictionary.Stem(new string(text));
        if (stems.Count == 0)
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            return;
        }

        if (_injectAlternates)
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            for (int j = 0; j < stems.Count; j++)
            {
                if (!string.Equals(stems[j], new string(text), StringComparison.OrdinalIgnoreCase))
                    sink.Add(stems[j].AsSpan(), startOffset, endOffset, type, 0, payload);
            }
        }
        else
        {
            sink.Add(stems[0].AsSpan(), startOffset, endOffset, type, positionIncrement, payload);
            for (int j = 1; j < stems.Count; j++)
                sink.Add(stems[j].AsSpan(), startOffset, endOffset, type, 0, payload);
        }
    }
}

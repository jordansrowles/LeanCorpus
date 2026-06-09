using Rowles.LeanCorpus.Analysis;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Emits bounded Latin-name phonetic alternates at the same token position.
/// </summary>
public sealed class PhoneticAlternatesFilter : ISpanTokenFilter
{
    private readonly bool _inject;
    private readonly int _maxExpansions;

    /// <summary>
    /// Initialises a new <see cref="PhoneticAlternatesFilter"/>.
    /// </summary>
    /// <param name="inject">When true, keeps the original token and appends phonetic alternates at the same position.</param>
    /// <param name="maxExpansions">Maximum number of emitted alternates per source token.</param>
    public PhoneticAlternatesFilter(bool inject = true, int maxExpansions = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExpansions, 1);
        _inject = inject;
        _maxExpansions = maxExpansions;
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
        var encodings = PhoneticEncoding.EncodeLatinNameAlternates(new string(text), _maxExpansions);
        if (encodings.Count == 0)
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            return;
        }

        if (_inject)
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            foreach (var encoding in encodings)
            {
                if (!string.Equals(encoding, new string(text), StringComparison.Ordinal))
                    sink.Add(encoding.AsSpan(), startOffset, endOffset, type, 0, payload);
            }
        }
        else
        {
            sink.Add(encodings[0].AsSpan(), startOffset, endOffset, type, positionIncrement, payload);
            for (int j = 1; j < encodings.Count; j++)
                sink.Add(encodings[j].AsSpan(), startOffset, endOffset, type, 0, payload);
        }
    }
}

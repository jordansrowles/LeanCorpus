using Rowles.LeanCorpus.Analysis;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Emits Metaphone encodings for tokens.
/// </summary>
public sealed class MetaphoneFilter : ISpanTokenFilter
{
    private readonly bool _inject;

    /// <summary>
    /// Initialises a new <see cref="MetaphoneFilter"/>.
    /// </summary>
    /// <param name="inject">When true, keeps the original token and appends the phonetic code at the same position.</param>
    public MetaphoneFilter(bool inject = true)
    {
        _inject = inject;
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
        string code = PhoneticEncoding.EncodeMetaphone(new string(text));
        if (string.IsNullOrEmpty(code))
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            return;
        }

        if (_inject)
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            if (!string.Equals(code, new string(text), StringComparison.Ordinal))
                sink.Add(code.AsSpan(), startOffset, endOffset, type, 0, payload);
        }
        else
        {
            sink.Add(code.AsSpan(), startOffset, endOffset, type, positionIncrement, payload);
        }
    }
}

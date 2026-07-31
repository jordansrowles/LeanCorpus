namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Normalises token position increments so same-position alternates remain explicit and
/// the stream stays consumable by LeanCorpus's linear postings model.
/// </summary>
public sealed class FlattenGraphFilter : ISpanTokenFilter
{

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
        sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
    }
}

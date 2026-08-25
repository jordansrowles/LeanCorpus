namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Normalises token position increments so same-position alternates remain explicit and
/// the stream stays consumable by LeanCorpus's linear postings model.
/// </summary>
public sealed class FlattenGraphFilter : ISpanTokenFilter
{
    private readonly TokenGraph _graph = new();

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
        Apply(text, startOffset, endOffset, type, positionIncrement, 1, payload, sink);
    }

    /// <inheritdoc/>
    public void Apply(ReadOnlySpan<char> text, int startOffset, int endOffset, string type,
        int positionIncrement, int positionLength, byte[]? payload, ISpanTokenSink sink)
    {
        _graph.Add(new Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload, positionLength));
    }

    /// <inheritdoc/>
    public void Finish(ISpanTokenSink sink)
    {
        _graph.Emit(sink, flatten: true);
        _graph.Clear();
    }

    /// <inheritdoc/>
    public ISpanTokenFilter Clone() => new FlattenGraphFilter();
}

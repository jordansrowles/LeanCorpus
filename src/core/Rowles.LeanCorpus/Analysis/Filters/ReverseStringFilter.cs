namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Reverses the characters in each token.
/// </summary>
public sealed class ReverseStringFilter : ISpanTokenFilter
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
        Span<char> reversed = stackalloc char[text.Length];
        text.CopyTo(reversed);
        reversed.Reverse();
        sink.Add(reversed, startOffset, endOffset, type, positionIncrement, payload);
    }
}

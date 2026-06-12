namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Forwards tokens to an inner sink with an added offset to start/end positions.
/// Used for Thai text segmentation where a sub-tokeniser operates on a slice
/// and the resulting offsets need to be adjusted back to the original input.
/// </summary>
internal sealed class OffsetAdjustingSink : Analysis.ISpanTokenSink
{
    private readonly Analysis.ISpanTokenSink _inner;
    private readonly int _offset;

    public OffsetAdjustingSink(Analysis.ISpanTokenSink inner, int offset)
    {
        _inner = inner;
        _offset = offset;
    }

    public void Add(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type = Analysis.Token.DefaultType,
        int positionIncrement = 1,
        byte[]? payload = null)
    {
        _inner.Add(text, startOffset + _offset, endOffset + _offset,
            type, positionIncrement, payload);
    }
}

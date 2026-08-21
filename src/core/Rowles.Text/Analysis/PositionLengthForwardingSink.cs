namespace Rowles.LeanCorpus.Analysis;

/// <summary>
/// Preserves an input edge length while adapting a legacy filter implementation.
/// </summary>
internal sealed class PositionLengthForwardingSink : ISpanTokenSink
{
    private readonly ISpanTokenSink _inner;
    private readonly int _positionLength;

    public PositionLengthForwardingSink(ISpanTokenSink inner, int positionLength)
    {
        _inner = inner;
        _positionLength = Token.ValidatePositionLength(positionLength);
    }

    public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset,
        string type = Token.DefaultType, int positionIncrement = 1, byte[]? payload = null) =>
        _inner.Add(text, startOffset, endOffset, type, positionIncrement, _positionLength, payload);

    public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type,
        int positionIncrement, int positionLength, byte[]? payload) =>
        _inner.Add(text, startOffset, endOffset, type, positionIncrement, positionLength, payload);
}

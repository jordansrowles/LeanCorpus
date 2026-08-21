namespace Rowles.LeanCorpus.Analysis;

/// <summary>
/// Materialised, absolute-position view of one analysed token stream.
/// </summary>
/// <remarks>
/// This is intentionally internal infrastructure rather than a second public analysis
/// API. It centralises the conversion between relative position increments and graph
/// edges for filters and query construction.
/// </remarks>
internal sealed class TokenGraph
{
    private readonly List<TokenEdge> _edges = [];

    public IReadOnlyList<TokenEdge> Edges => _edges;

    public void Add(Token token)
    {
        int start = _edges.Count == 0
            ? Math.Max(0, token.PositionIncrement - 1)
            : _edges[^1].StartPosition + Math.Max(0, token.PositionIncrement);
        Add(token, start);
    }

    public void Add(Token token, int startPosition)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startPosition);
        _edges.Add(new TokenEdge(token, startPosition, _edges.Count));
    }

    public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type,
        int startPosition, int positionLength, byte[]? payload = null) =>
        Add(new Token(text.ToString(), startOffset, endOffset, type, 1, payload, positionLength), startPosition);

    public void ValidateOrdered()
    {
        int previousStart = -1;
        foreach (var edge in _edges)
        {
            if (edge.StartPosition < previousStart)
                throw new InvalidOperationException("Token graph edges must be emitted in non-decreasing start-position order.");
            previousStart = edge.StartPosition;
        }
    }

    public void Clear() => _edges.Clear();

    public void Emit(ISpanTokenSink sink, bool flatten = false)
    {
        ArgumentNullException.ThrowIfNull(sink);
        int previousPosition = -1;
        foreach (var edge in _edges.OrderBy(static edge => edge.StartPosition).ThenBy(static edge => edge.Order))
        {
            int increment = edge.StartPosition - previousPosition;
            sink.Add(edge.Token.Text.AsSpan(), edge.Token.StartOffset, edge.Token.EndOffset,
                edge.Token.Type, increment, flatten ? 1 : edge.Token.PositionLength, edge.Token.Payload);
            previousPosition = edge.StartPosition;
        }
    }

    public readonly record struct TokenEdge(Token Token, int StartPosition, int Order)
    {
        public int EndPosition => StartPosition + Token.PositionLength;
    }
}

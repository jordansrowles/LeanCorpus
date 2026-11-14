namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Token filter that supports multi-token synonym expansion using a trie-based
/// <see cref="SynonymMap"/>. Uses longest-match lookahead for multi-word synonyms
/// and inserts replacement tokens at the same position offsets.
/// </summary>
/// <remarks>
/// Tokens that cannot begin a synonym source pass straight through without being copied.
/// A token that can begin one is held, with at most the map's longest source as look-ahead,
/// until the longest match is decided. Output is identical to buffering the whole stream:
/// edges are emitted in start-position order, original tokens before the replacements that
/// start at the same position, and replacements in match order.
/// </remarks>
public sealed class SynonymGraphFilter : ISpanTokenFilter
{
    private readonly SynonymMap _map;

    // Held edges in emission order: start position, then original before replacement, then arrival.
    private readonly List<PendingEdge> _pending = new();

    // Input tokens whose match decision is still open, oldest first.
    private readonly List<PendingEdge> _window = new();

    private bool _hasInput;
    private int _lastStart;
    private int _previousEmittedStart = -1;

    /// <summary>
    /// Initialises a new <see cref="SynonymGraphFilter"/> with the specified synonym map.
    /// </summary>
    /// <param name="map">The synonym map used for multi-token expansion lookups.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="map"/> is <see langword="null"/>.</exception>
    public SynonymGraphFilter(SynonymMap map)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
    }

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
        ArgumentNullException.ThrowIfNull(sink);
        Accept(text, startOffset, endOffset, type, positionIncrement, 1, payload, sink);
    }

    /// <inheritdoc/>
    public void Apply(ReadOnlySpan<char> text, int startOffset, int endOffset, string type,
        int positionIncrement, int positionLength, byte[]? payload, ISpanTokenSink sink)
    {
        Accept(text, startOffset, endOffset, type, positionIncrement, positionLength, payload, sink);
    }

    /// <inheritdoc/>
    public void Finish(ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (_hasInput)
        {
            Resolve(final: true);
            Flush(sink, final: true);
        }

        _pending.Clear();
        _window.Clear();
        _hasInput = false;
        _lastStart = 0;
        _previousEmittedStart = -1;
    }

    /// <inheritdoc/>
    public ISpanTokenFilter Clone() => new SynonymGraphFilter(_map);

    private void Accept(ReadOnlySpan<char> text, int startOffset, int endOffset, string type,
        int positionIncrement, int positionLength, byte[]? payload, ISpanTokenSink sink)
    {
        // Same absolute start as TokenGraph.Add(Token): the first token starts at increment - 1,
        // later ones advance by a non-negative increment.
        int start = _hasInput
            ? _lastStart + Math.Max(0, positionIncrement)
            : Math.Max(0, positionIncrement - 1);
        if (_hasInput && start < _lastStart)
            throw new InvalidOperationException("Token graph edges must be emitted in non-decreasing start-position order.");
        _hasInput = true;
        _lastStart = start;

        // A later start can release replacements held for an earlier position.
        Flush(sink, final: false);

        if (_window.Count == 0 && _pending.Count == 0 && !_map.IsSourceStart(text))
        {
            // Nothing is held and this token cannot begin a match, so its decision is final
            // and nothing can be emitted before it.
            Token.ValidatePositionLength(positionLength);
            sink.Add(text, startOffset, endOffset, Token.ValidateType(type),
                start - _previousEmittedStart, positionLength, payload);
            _previousEmittedStart = start;
            return;
        }

        var edge = new PendingEdge(
            new Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload, positionLength),
            start,
            IsReplacement: false);
        InsertOriginal(edge);
        _window.Add(edge);
        Resolve(final: false);
        Flush(sink, final: false);
    }

    /// <summary>Decides every held match whose outcome later tokens can no longer change.</summary>
    private void Resolve(bool final)
    {
        while (_window.Count > 0)
        {
            int matchLength = _map.TryMatch(_window, out var replacements, out bool canExtend);
            if (canExtend && !final)
                return;

            if (matchLength == 0)
            {
                _window.RemoveAt(0);
                continue;
            }

            var first = _window[0];
            var last = _window[matchLength - 1];
            int positionLength = last.Start + last.Token.PositionLength - first.Start;
            for (int r = 0; r < replacements!.Length; r++)
            {
                InsertReplacement(new PendingEdge(
                    new Token(replacements[r], first.Token.StartOffset, last.Token.EndOffset,
                        first.Token.Type, 1, null, positionLength),
                    first.Start,
                    IsReplacement: true));
            }
            _window.RemoveRange(0, matchLength);
        }
    }

    /// <summary>
    /// Emits held edges that no later edge can precede. A later original token starts at or
    /// after the last start seen, and sorts after held originals at that start but before held
    /// replacements there. A later replacement starts at or after the oldest open window token.
    /// </summary>
    private void Flush(ISpanTokenSink sink, bool final)
    {
        int openStart = _window.Count > 0 ? _window[0].Start : int.MaxValue;
        int emitted = 0;
        foreach (var edge in _pending)
        {
            if (!final)
            {
                if (edge.Start > openStart)
                    break;
                if (edge.IsReplacement && edge.Start >= _lastStart)
                    break;
            }

            var token = edge.Token;
            sink.Add(token.Text.AsSpan(), token.StartOffset, token.EndOffset, token.Type,
                edge.Start - _previousEmittedStart, token.PositionLength, token.Payload);
            _previousEmittedStart = edge.Start;
            emitted++;
        }

        if (emitted > 0)
            _pending.RemoveRange(0, emitted);
    }

    private void InsertOriginal(PendingEdge edge)
    {
        // Originals sort before held replacements at the same start.
        int index = _pending.Count;
        while (index > 0 && _pending[index - 1].IsReplacement && _pending[index - 1].Start == edge.Start)
            index--;
        _pending.Insert(index, edge);
    }

    private void InsertReplacement(PendingEdge edge)
    {
        // Replacements sort after every held edge at the same or an earlier start.
        int index = _pending.Count;
        while (index > 0 && _pending[index - 1].Start > edge.Start)
            index--;
        _pending.Insert(index, edge);
    }

    internal readonly record struct PendingEdge(Token Token, int Start, bool IsReplacement);
}

namespace Rowles.LeanCorpus.Analysis.Tokenisers;

using Rowles.LeanCorpus.Analysis;

/// <summary>
/// Splits text into character substrings of length [<see cref="MinGram"/>, <see cref="MaxGram"/>]
/// anchored at the start of each whitespace-delimited token (edge n-grams), using
/// <see cref="char.IsWhiteSpace(char)"/> for Unicode-aware whitespace detection.
///
/// Thread-safety: the span path and enumerator are thread-safe for concurrent use on the same instance.
/// No per-instance mutable state is retained across calls.
/// </summary>
public sealed class EdgeNGramTokeniser : ISpanTokeniser
{
    /// <summary>
    /// Gets the minimum n-gram length (inclusive).
    /// </summary>
    public int MinGram { get; }

    /// <summary>
    /// Gets the maximum n-gram length (inclusive).
    /// </summary>
    public int MaxGram { get; }

    /// <summary>
    /// Initialises a new <see cref="EdgeNGramTokeniser"/> with the specified gram size range.
    /// </summary>
    /// <param name="minGram">The minimum gram length (must be ≥ 1).</param>
    /// <param name="maxGram">The maximum gram length (must be ≥ <paramref name="minGram"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="minGram"/> is less than 1, or <paramref name="maxGram"/> is less than <paramref name="minGram"/>.
    /// </exception>
    public EdgeNGramTokeniser(int minGram, int maxGram)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minGram, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxGram, minGram);
        MinGram = minGram;
        MaxGram = maxGram;
    }

    /// <inheritdoc/>
    public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Emit(input, sink);
    }

    /// <summary>
    /// Returns a stack-only <see cref="Enumerator"/> that yields edge n-gram tokens
    /// one at a time without materialising a <see cref="List{Token}"/> or token text strings.
    /// Use in a <c>foreach</c> loop when early termination or zero-list-allocation
    /// enumeration is desired.
    /// </summary>
    /// <param name="input">The text to tokenise.</param>
    public Enumerator EnumerateTokens(ReadOnlySpan<char> input) => new(this, input);

    /// <summary>
    /// Stack-only edge n-gram enumerator. Each call to <see cref="MoveNext"/> advances
    /// to the next edge n-gram. <see cref="Current"/> exposes the yielded token.
    /// </summary>
    public ref struct Enumerator
    {
        private readonly EdgeNGramTokeniser _owner;
        private readonly ReadOnlySpan<char> _input;
        private int _scanPos;
        private int _wordStart;
        private int _wordEnd;
        private int _gramLen;
        private SpanToken _current;

        internal Enumerator(EdgeNGramTokeniser owner, ReadOnlySpan<char> input)
        {
            _owner = owner;
            _input = input;
            _scanPos = 0;
            _wordStart = 0;
            _wordEnd = 0;
            _gramLen = 0;
            _current = default;
        }

        /// <summary>Gets the current token.</summary>
        public SpanToken Current => _current;

        /// <summary>Advances to the next edge n-gram token.</summary>
        public bool MoveNext()
        {
            while (true)
            {
                // Find next word if we haven't started one or finished the previous
                if (_gramLen == 0 && _scanPos < _input.Length)
                {
                    while (_scanPos < _input.Length && char.IsWhiteSpace(_input[_scanPos]))
                        _scanPos++;

                    if (_scanPos >= _input.Length)
                        return false;

                    _wordStart = _scanPos;
                    while (_scanPos < _input.Length && !char.IsWhiteSpace(_input[_scanPos]))
                        _scanPos++;
                    _wordEnd = _scanPos;
                }

                int tokenLen = _wordEnd - _wordStart;
                if (tokenLen < _owner.MinGram)
                {
                    _gramLen = 0;
                    continue;
                }

                int maxGramLen = Math.Min(_owner.MaxGram, tokenLen);

                _gramLen++;
                if (_gramLen >= _owner.MinGram && _gramLen <= maxGramLen)
                {
                    _current = new SpanToken(_input.Slice(_wordStart, _gramLen), _wordStart, _wordStart + _gramLen);
                    return true;
                }

                // Done with this word
                _gramLen = 0;
            }
        }

        /// <summary>Returns <c>this</c> for <c>foreach</c> support.</summary>
        public Enumerator GetEnumerator() => this;
    }

    private void Emit(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        int i = 0;
        while (i < input.Length)
        {
            // Skip whitespace
            while (i < input.Length && char.IsWhiteSpace(input[i]))
                i++;

            if (i >= input.Length)
                break;

            int wordStart = i;
            while (i < input.Length && !char.IsWhiteSpace(input[i]))
                i++;

            int tokenLen = i - wordStart;
            int maxGramLen = Math.Min(MaxGram, tokenLen);
            for (int gramLen = MinGram; gramLen <= maxGramLen; gramLen++)
            {
                sink.Add(input.Slice(wordStart, gramLen), wordStart, wordStart + gramLen);
            }
        }
    }
}

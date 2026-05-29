namespace Rowles.LeanCorpus.Analysis.Tokenisers;

using Rowles.LeanCorpus.Analysis;

/// <summary>
/// Splits text into all contiguous character substrings of length in [<see cref="MinGram"/>, <see cref="MaxGram"/>].
/// Useful for partial-word matching and CJK text.
///
/// When <see cref="SplitOnWhitespace"/> is <see langword="true"/> the tokeniser first splits on
/// whitespace (via <see cref="char.IsWhiteSpace(char)"/>) and applies n-grams per word only,
/// which avoids cross-word-boundary grams.
///
/// Thread-safety: the span path and enumerator are thread-safe for concurrent use on the same instance.
/// No per-instance mutable state is retained across calls.
/// </summary>
public sealed class NGramTokeniser : ISpanTokeniser
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
    /// Gets whether the tokeniser splits on whitespace before applying n-grams.
    /// When <see langword="true"/>, no gram spans a word boundary.
    /// </summary>
    public bool SplitOnWhitespace { get; }

    /// <summary>
    /// Initialises a new <see cref="NGramTokeniser"/> with the specified gram size range.
    /// </summary>
    /// <param name="minGram">The minimum gram length (must be ≥ 1).</param>
    /// <param name="maxGram">The maximum gram length (must be ≥ <paramref name="minGram"/>).</param>
    /// <param name="splitOnWhitespace">
    /// When <see langword="true"/>, n-grams are generated per whitespace-delimited word rather than
    /// across the entire input. Defaults to <see langword="false"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="minGram"/> is less than 1, or <paramref name="maxGram"/> is less than <paramref name="minGram"/>.
    /// </exception>
    public NGramTokeniser(int minGram, int maxGram, bool splitOnWhitespace = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minGram, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxGram, minGram);
        MinGram = minGram;
        MaxGram = maxGram;
        SplitOnWhitespace = splitOnWhitespace;
    }

    /// <inheritdoc/>
    public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (SplitOnWhitespace)
            EmitSplit(input, sink);
        else
            EmitFull(input, sink);
    }

    /// <summary>
    /// Returns a stack-only <see cref="Enumerator"/> that yields n-gram tokens
    /// one at a time without materialising a <see cref="List{Token}"/> or token text strings.
    /// When <see cref="SplitOnWhitespace"/> is <see langword="true"/>, n-grams are
    /// generated per word; otherwise they span the full input.
    /// Use in a <c>foreach</c> loop for zero-list-allocation enumeration.
    /// </summary>
    /// <param name="input">The text to tokenise.</param>
    public Enumerator EnumerateTokens(ReadOnlySpan<char> input) => new(this, input);

    /// <summary>
    /// Stack-only n-gram enumerator. Each call to <see cref="MoveNext"/> yields the
    /// next n-gram in increasing start-offset order.
    /// </summary>
    public ref struct Enumerator
    {
        private readonly NGramTokeniser _owner;
        private readonly ReadOnlySpan<char> _input;
        private int _scanPos;
        private int _wordStart;
        private int _wordEnd;
        private int _start;
        private int _gramLen;
        private SpanToken _current;

        internal Enumerator(NGramTokeniser owner, ReadOnlySpan<char> input)
        {
            _owner = owner;
            _input = input;
            _scanPos = 0;
            _wordStart = 0;
            _wordEnd = 0;
            _start = 0;
            _gramLen = owner.MinGram - 1;
            _current = default;
        }

        /// <summary>Gets the current token.</summary>
        public SpanToken Current => _current;

        /// <summary>Advances to the next n-gram token.</summary>
        public bool MoveNext()
        {
            if (_owner.SplitOnWhitespace)
                return MoveNextSplit();
            return MoveNextFull();
        }

        private bool MoveNextSplit()
        {
            while (true)
            {
                // Find next word if needed
                if (_start == 0 && _gramLen == _owner.MinGram - 1)
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

                int wordLen = _wordEnd - _wordStart;

                _gramLen++;
                if (_gramLen <= _owner.MaxGram && _start + _gramLen <= wordLen)
                {
                    int absStart = _wordStart + _start;
                    var span = _input.Slice(absStart, _gramLen);
                    _current = new SpanToken(span, absStart, absStart + _gramLen);
                    return true;
                }

                _start++;
                _gramLen = _owner.MinGram - 1;

                if (_start >= wordLen)
                    _start = 0;
            }
        }

        private bool MoveNextFull()
        {
            int len = _input.Length;
            while (_start < len)
            {
                _gramLen++;
                if (_gramLen <= _owner.MaxGram && _start + _gramLen <= len)
                {
                    var span = _input.Slice(_start, _gramLen);
                    _current = new SpanToken(span, _start, _start + _gramLen);
                    return true;
                }

                _start++;
                _gramLen = _owner.MinGram - 1;
            }

            return false;
        }

        /// <summary>Returns <c>this</c> for <c>foreach</c> support.</summary>
        public Enumerator GetEnumerator() => this;
    }

    private void EmitFull(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        int len = input.Length;
        for (int start = 0; start < len; start++)
        {
            for (int gramLen = MinGram; gramLen <= MaxGram && start + gramLen <= len; gramLen++)
            {
                sink.Add(input.Slice(start, gramLen), start, start + gramLen);
            }
        }
    }

    private void EmitSplit(ReadOnlySpan<char> input, ISpanTokenSink sink)
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

            int wordLen = i - wordStart;
            for (int start = 0; start < wordLen; start++)
            {
                for (int gramLen = MinGram; gramLen <= MaxGram && start + gramLen <= wordLen; gramLen++)
                {
                    int absStart = wordStart + start;
                    sink.Add(input.Slice(absStart, gramLen), absStart, absStart + gramLen);
                }
            }
        }
    }
}

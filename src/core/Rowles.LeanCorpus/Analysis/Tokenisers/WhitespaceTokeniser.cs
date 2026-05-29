namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Splits input text into tokens separated only by whitespace.
/// </summary>
public sealed class WhitespaceTokeniser : ITokeniser
{
    /// <inheritdoc/>
    public List<Token> Tokenise(ReadOnlySpan<char> input)
    {
        var tokens = new List<Token>();
        TokeniseOffsets(input, tokens);
        return tokens;
    }

    /// <summary>
    /// Returns a stack-only <see cref="Enumerator"/> that yields whitespace-delimited
    /// tokens one at a time without materialising a <see cref="List{Token}"/>.
    /// Use in a <c>foreach</c> loop for zero-list-allocation enumeration.
    /// </summary>
    /// <param name="input">The text to tokenise.</param>
    public Enumerator EnumerateTokens(ReadOnlySpan<char> input) => new(input);

    /// <summary>
    /// Stack-only whitespace token enumerator.
    /// </summary>
    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<char> _input;
        private int _pos;
        private Token _current;

        internal Enumerator(ReadOnlySpan<char> input)
        {
            _input = input;
            _pos = 0;
            _current = default;
        }

        /// <summary>Gets the current token.</summary>
        public Token Current => _current;

        /// <summary>Advances to the next whitespace-delimited token.</summary>
        public bool MoveNext()
        {
            while (_pos < _input.Length && char.IsWhiteSpace(_input[_pos]))
                _pos++;

            if (_pos >= _input.Length)
                return false;

            int start = _pos;
            while (_pos < _input.Length && !char.IsWhiteSpace(_input[_pos]))
                _pos++;

            _current = new Token(_input[start.._pos].ToString(), start, _pos);
            return true;
        }

        /// <summary>Returns <c>this</c> for <c>foreach</c> support.</summary>
        public Enumerator GetEnumerator() => this;
    }

    /// <summary>
    /// Emits whitespace-delimited tokens into the supplied list.
    /// </summary>
    /// <param name="input">The text to tokenise.</param>
    /// <param name="tokens">The list to populate. Cleared before use.</param>
    public void TokeniseOffsets(ReadOnlySpan<char> input, List<Token> tokens)
    {
        tokens.Clear();
        int i = 0;

        while (i < input.Length)
        {
            while (i < input.Length && char.IsWhiteSpace(input[i]))
                i++;

            if (i >= input.Length)
                break;

            int start = i;
            while (i < input.Length && !char.IsWhiteSpace(input[i]))
                i++;

            tokens.Add(new Token(input[start..i].ToString(), start, i));
        }
    }

    /// <summary>
    /// Emits whitespace-delimited token offsets into the supplied list without materialising token text.
    /// </summary>
    /// <param name="input">The text to tokenise.</param>
    /// <param name="offsets">The list to populate. Cleared before use.</param>
    internal void TokeniseOffsets(ReadOnlySpan<char> input, List<(int Start, int End)> offsets)
    {
        offsets.Clear();
        int i = 0;

        while (i < input.Length)
        {
            while (i < input.Length && char.IsWhiteSpace(input[i]))
                i++;

            if (i >= input.Length)
                break;

            int start = i;
            while (i < input.Length && !char.IsWhiteSpace(input[i]))
                i++;

            offsets.Add((start, i));
        }
    }
}

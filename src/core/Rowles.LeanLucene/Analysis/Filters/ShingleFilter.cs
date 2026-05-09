namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Emits contiguous token shingles for phrase-oriented analysis.
/// </summary>
public sealed class ShingleFilter : ITokenFilter
{
    private readonly int _minShingleSize;
    private readonly int _maxShingleSize;
    private readonly bool _outputUnigrams;
    private readonly string _tokenSeparator;

    /// <summary>
    /// Initialises a new <see cref="ShingleFilter"/>.
    /// </summary>
    /// <param name="minShingleSize">The minimum number of source tokens in a shingle.</param>
    /// <param name="maxShingleSize">The maximum number of source tokens in a shingle.</param>
    /// <param name="outputUnigrams">Whether original tokens should remain in the output.</param>
    /// <param name="tokenSeparator">The separator inserted between token texts.</param>
    public ShingleFilter(int minShingleSize = 2, int maxShingleSize = 2, bool outputUnigrams = true, string tokenSeparator = " ")
    {
        if (minShingleSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(minShingleSize));
        if (maxShingleSize < minShingleSize)
            throw new ArgumentOutOfRangeException(nameof(maxShingleSize));
        ArgumentNullException.ThrowIfNull(tokenSeparator);

        _minShingleSize = minShingleSize;
        _maxShingleSize = maxShingleSize;
        _outputUnigrams = outputUnigrams;
        _tokenSeparator = tokenSeparator;
    }

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        if (tokens.Count == 0)
            return;

        int shingleCapacity = 0;
        for (int size = _minShingleSize; size <= _maxShingleSize; size++)
        {
            if (tokens.Count >= size)
                shingleCapacity += tokens.Count - size + 1;
        }

        if (shingleCapacity == 0 && _outputUnigrams)
            return;

        var result = new List<Token>(_outputUnigrams ? tokens.Count + shingleCapacity : shingleCapacity);
        if (_outputUnigrams)
            result.AddRange(tokens);

        for (int start = 0; start < tokens.Count; start++)
        {
            int remaining = tokens.Count - start;
            int maxSize = Math.Min(_maxShingleSize, remaining);
            for (int size = _minShingleSize; size <= maxSize; size++)
            {
                string text = CreateShingle(tokens, start, size, _tokenSeparator);
                result.Add(new Token(text, tokens[start].StartOffset, tokens[start + size - 1].EndOffset));
            }
        }

        tokens.Clear();
        tokens.AddRange(result);
    }

    private static string CreateShingle(List<Token> tokens, int start, int count, string separator)
    {
        int length = separator.Length * (count - 1);
        for (int i = 0; i < count; i++)
            length += tokens[start + i].Text.Length;

        return string.Create(length, (tokens, start, count, separator), static (buffer, state) =>
        {
            int offset = 0;
            for (int i = 0; i < state.count; i++)
            {
                if (i > 0)
                {
                    state.separator.AsSpan().CopyTo(buffer[offset..]);
                    offset += state.separator.Length;
                }

                string text = state.tokens[state.start + i].Text;
                text.AsSpan().CopyTo(buffer[offset..]);
                offset += text.Length;
            }
        });
    }
}

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Emits contiguous token shingles for phrase-oriented analysis.
/// </summary>
/// <remarks>
/// The <see cref="ISpanTokenFilter"/> implementation passes tokens through without
/// shingle expansion because shingle generation requires a sliding window over multiple
/// tokens and a flush mechanism that the one-at-a-time span interface cannot provide.
/// Callers that need shingle expansion should use a dedicated analyser that works on
/// the full token list.
/// </remarks>
public sealed class ShingleFilter : ISpanTokenFilter
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

    public void Apply(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type,
        int positionIncrement,
        byte[]? payload,
        ISpanTokenSink sink)
    {
        sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
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

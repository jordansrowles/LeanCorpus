namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Emits contiguous token shingles for phrase-oriented analysis.
/// </summary>
/// <remarks>
/// Buffers tokens during <see cref="Apply"/> and generates shingles covering
/// <see cref="MinShingleSize"/> to <see cref="MaxShingleSize"/> tokens in
/// <see cref="ISpanTokenFilter.Finish"/>. When <see cref="OutputUnigrams"/> is
/// <c>true</c>, original tokens are emitted alongside shingles.
/// </remarks>
public sealed class ShingleFilter : ISpanTokenFilter
{
    private readonly int _minShingleSize;
    private readonly int _maxShingleSize;
    private readonly bool _outputUnigrams;
    private readonly string _tokenSeparator;
    private readonly List<Token> _buffer = new();

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

    /// <summary>
    /// Gets the minimum number of source tokens in a shingle.
    /// </summary>
    public int MinShingleSize => _minShingleSize;

    /// <summary>
    /// Gets the maximum number of source tokens in a shingle.
    /// </summary>
    public int MaxShingleSize => _maxShingleSize;

    /// <summary>
    /// Gets whether original tokens are emitted alongside shingles.
    /// </summary>
    public bool OutputUnigrams => _outputUnigrams;

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

        // Materialise the text — the span is transient.
        var token = new Token(
            text.ToString(),
            startOffset,
            endOffset,
            type,
            positionIncrement,
            payload);

        _buffer.Add(token);

        if (_outputUnigrams)
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
    }

    /// <inheritdoc/>
    public void Finish(ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (_buffer.Count == 0)
            return;

        int maxSize = Math.Min(_maxShingleSize, _buffer.Count);

        for (int size = _minShingleSize; size <= maxSize; size++)
        {
            for (int start = 0; start <= _buffer.Count - size; start++)
            {
                var shingleText = CreateShingle(start, size);

                var first = _buffer[start];
                var last = _buffer[start + size - 1];

                sink.Add(
                    shingleText.AsSpan(),
                    first.StartOffset,
                    last.EndOffset,
                    first.Type,
                    positionIncrement: 0,
                    payload: null);
            }
        }

        _buffer.Clear();
    }

    private string CreateShingle(int start, int count)
    {
        string separator = _tokenSeparator;
        int sepLen = separator.Length;
        int totalLength = sepLen * (count - 1);
        for (int i = 0; i < count; i++)
            totalLength += _buffer[start + i].Text.Length;

        return string.Create(totalLength, (start, count, separator, _buffer), static (buffer, state) =>
        {
            int offset = 0;
            for (int i = 0; i < state.count; i++)
            {
                if (i > 0)
                {
                    state.separator.AsSpan().CopyTo(buffer[offset..]);
                    offset += state.separator.Length;
                }

                string text = state._buffer[state.start + i].Text;
                text.AsSpan().CopyTo(buffer[offset..]);
                offset += text.Length;
            }
        });
    }
}

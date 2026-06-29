using System.Buffers;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Joins consecutive tokens that share the same position into a single hyphenated token.
/// </summary>
/// <remarks>
/// <para>When a tokeniser splits hyphenated compounds into separate tokens at the same
/// position (signalled by <c>positionIncrement == 0</c>), this filter reverses the
/// split by joining those tokens back together with a separator character between them.</para>
/// <para>For example, if <c>"state-of-the-art"</c> is tokenised as four tokens at the
/// same position — <c>"state"</c>, <c>"of"</c>, <c>"the"</c>, <c>"art"</c> — this
/// filter emits a single token <c>"state-of-the-art"</c>.</para>
/// <para>Uses <see cref="ArrayPool{T}"/> for the accumulation buffer and calls
/// <see cref="ISpanTokenFilter.Finish"/> to flush the final buffered token at
/// end-of-stream.</para>
/// </remarks>
public sealed class HyphenatedWordsFilter : ISpanTokenFilter
{
    private readonly char _separator;
    private readonly int _initialCapacity;

    // State — mutable across Apply calls.
    private char[]? _buffer;
    private int _bufferLength;
    private int _startOffset;
    private int _endOffset;
    private string _type = Token.DefaultType;
    private int _positionIncrement;
    private byte[]? _payload;
    private bool _hasBuffered;

    /// <summary>
    /// Initialises a new <see cref="HyphenatedWordsFilter"/>.
    /// </summary>
    /// <param name="separator">The character inserted between joined token texts.
    /// Defaults to <c>'-'</c>.</param>
    /// <param name="initialCapacity">Initial capacity hint for the accumulation buffer.
    /// Defaults to 64 characters.</param>
    public HyphenatedWordsFilter(char separator = '-', int initialCapacity = 64)
    {
        _separator = separator;
        _initialCapacity = Math.Max(initialCapacity, 16);
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

        if (_hasBuffered && positionIncrement == 0)
        {
            // Same position as the previous token — extend the buffer.
            AppendSeparatorAndText(text);
            _endOffset = endOffset;
        }
        else
        {
            // New position or first token — flush the old buffer first.
            Flush(sink);

            // Start buffering this token.
            AppendText(text);
            _startOffset = startOffset;
            _endOffset = endOffset;
            _type = type;
            _positionIncrement = positionIncrement;
            _payload = payload;
            _hasBuffered = true;
        }
    }

    /// <inheritdoc/>
    public void Finish(ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Flush(sink);
    }

    private void AppendText(ReadOnlySpan<char> text)
    {
        EnsureCapacity(text.Length);
        text.CopyTo(_buffer.AsSpan(_bufferLength));
        _bufferLength += text.Length;
    }

    private void AppendSeparatorAndText(ReadOnlySpan<char> text)
    {
        int needed = 1 + text.Length;
        EnsureCapacity(needed);
        _buffer![_bufferLength] = _separator;
        _bufferLength++;
        text.CopyTo(_buffer.AsSpan(_bufferLength));
        _bufferLength += text.Length;
    }

    private void EnsureCapacity(int additional)
    {
        int needed = _bufferLength + additional;
        if (_buffer is not null && needed <= _buffer.Length)
            return;

        int newCapacity = _buffer is null
            ? Math.Max(_initialCapacity, needed)
            : Math.Max(_buffer.Length * 2, needed);

        char[] newBuffer = ArrayPool<char>.Shared.Rent(newCapacity);
        if (_buffer is not null)
        {
            _buffer.AsSpan(0, _bufferLength).CopyTo(newBuffer);
            ArrayPool<char>.Shared.Return(_buffer);
        }

        _buffer = newBuffer;
    }

    private void Flush(ISpanTokenSink sink)
    {
        if (!_hasBuffered || _buffer is null)
            return;

        ReadOnlySpan<char> joined = _buffer.AsSpan(0, _bufferLength);
        sink.Add(joined, _startOffset, _endOffset, _type, _positionIncrement, _payload);

        // Return the buffer and reset state.
        _bufferLength = 0;
        _hasBuffered = false;
    }
}

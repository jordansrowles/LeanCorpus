using System.Buffers;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Adapters;

/// <summary>
/// Adapts an <c>IndexOutput</c> to <c>IBufferWriter&lt;byte&gt;</c> so
/// CodecKit can encode directly into LeanCorpus's buffered file writer.
/// Uses a small internal buffer; <see cref="Advance"/> flushes filled bytes
/// to the underlying output.
/// </summary>
internal sealed class IndexOutputBuffer : IBufferWriter<byte>
{
    private readonly IndexOutput _output;
    private byte[] _buffer;
    private int _position;

    public IndexOutputBuffer(IndexOutput output)
    {
        _output = output;
        _buffer = ArrayPool<byte>.Shared.Rent(4096);
        _position = 0;
    }

    public void Advance(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count > _buffer.Length - _position)
            throw new InvalidOperationException("Advanced past end of buffer.");
        _output.WriteBytes(_buffer.AsSpan(_position, count));
        _position = 0;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(Math.Max(sizeHint, 256));
        return _buffer.AsMemory(_position);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(Math.Max(sizeHint, 256));
        return _buffer.AsSpan(_position);
    }

    private void EnsureCapacity(int required)
    {
        int remaining = _buffer.Length - _position;
        if (remaining >= required) return;

        int newSize = _buffer.Length;
        while (newSize - _position < required)
            newSize *= 2;
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
        _buffer.AsSpan(0, _position).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }

    /// <summary>Direct write, preferred for bulk data.</summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        _output.WriteBytes(data);
    }

    public void Dispose()
    {
        if (_buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null!;
        }
    }
}

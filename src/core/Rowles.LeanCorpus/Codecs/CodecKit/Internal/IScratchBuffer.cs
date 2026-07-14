using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// A scratch buffer backed by pooled arrays, used for transactional encode staging.
/// Implements <see cref="IBufferWriter{T}"/> for writing and exposes <see cref="Written"/>
/// for reading back the staged bytes.
/// </summary>
internal interface IScratchBuffer : IBufferWriter<byte>, IDisposable
{
    /// <summary>The bytes written so far as a contiguous or segmented sequence.</summary>
    ReadOnlySequence<byte> Written { get; }

    /// <summary>Total number of bytes written.</summary>
    int Length { get; }

    /// <summary>Resets the buffer for reuse without returning pooled memory.</summary>
    void Reset();
}

internal sealed class ArrayPoolScratchBuffer : IScratchBuffer
{
    private byte[] _buffer;
    private int _position;
    private readonly int _limit;

    public ArrayPoolScratchBuffer(int initialCapacity, int limit = 0)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 256));
        _position = 0;
        _limit = limit;
    }

    public ReadOnlySequence<byte> Written =>
        new ReadOnlySequence<byte>(_buffer, 0, _position);

    public int Length => _position;

    public void Advance(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (_position + count > _buffer.Length) throw new InvalidOperationException("Advanced past the end of the buffer.");
        _position += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_position);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_position);
    }

    public void Reset() => _position = 0;

    public void Dispose()
    {
        var buf = _buffer;
        _buffer = Array.Empty<byte>();
        _position = 0;
        if (buf.Length > 0)
            ArrayPool<byte>.Shared.Return(buf);
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint <= 0) sizeHint = 1;
        int required = _position + sizeHint;
        if (_limit > 0 && required > _limit)
            throw new LimitExceededException(CodecErrorCode.FrameTooLarge, 0, string.Empty,
                "ScratchBuffer", required, _limit);
        if (required <= _buffer.Length) return;

        int newSize = Math.Max(_buffer.Length * 2, required);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
        _buffer.AsSpan(0, _position).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }
}

using System.Buffers;
using Rowles.LeanCorpus.Index.Segment;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;

internal sealed class ShapePrimitiveByteBuffer : IDisposable
{
    private readonly ArrayPool<byte> _pool;
    private readonly Action<long>? _allocationChanged;
    private byte[]? _buffer;
    private int _length;
    private int _disposed;

    internal ShapePrimitiveByteBuffer(Action<long>? allocationChanged = null, ArrayPool<byte>? pool = null)
    {
        _allocationChanged = allocationChanged;
        _pool = pool ?? ArrayPool<byte>.Shared;
    }

    internal int Length => _length;
    internal long AllocatedBytes => _buffer?.LongLength ?? 0;

    internal void Append(ShapePrimitive primitive, SpatialFieldKind fieldKind)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        int required = checked(_length + ShapePrimitiveCodec.PackedValueLength);
        EnsureCapacity(required);
        Span<byte> destination = _buffer!.AsSpan(_length, ShapePrimitiveCodec.PackedValueLength);
        switch (primitive.Kind)
        {
            case ShapePrimitiveKind.Point:
                ShapePrimitiveCodec.EncodePoint(destination, fieldKind, primitive.A, primitive.ValueOrdinal);
                break;
            case ShapePrimitiveKind.Line:
                ShapePrimitiveCodec.EncodeLine(destination, fieldKind, primitive.A, primitive.B, primitive.ValueOrdinal);
                break;
            case ShapePrimitiveKind.Triangle:
                ShapePrimitiveCodec.EncodeTriangle(
                    destination,
                    fieldKind,
                    primitive.A, primitive.EdgeAB,
                    primitive.B, primitive.EdgeBC,
                    primitive.C, primitive.EdgeCA,
                    primitive.ValueOrdinal);
                break;
            default:
                throw new InvalidDataException($"Unsupported shape primitive kind '{primitive.Kind}'.");
        }

        _length = required;
    }

    internal void AppendEncoded(ReadOnlySpan<byte> packedPrimitives)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (packedPrimitives.Length == 0 || packedPrimitives.Length % ShapePrimitiveCodec.PackedValueLength != 0)
            throw new ArgumentException("Encoded shape primitives must contain complete 28-byte values.", nameof(packedPrimitives));
        int required = checked(_length + packedPrimitives.Length);
        EnsureCapacity(required);
        packedPrimitives.CopyTo(_buffer!.AsSpan(_length, packedPrimitives.Length));
        _length = required;
    }

    internal ReadOnlyMemory<byte> GetMemory(int offset, int length)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        byte[] buffer = _buffer ?? throw new InvalidDataException("The prepared shape buffer is empty.");
        if (offset < 0 || length < 0 || (long)offset + length > _length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return new ReadOnlyMemory<byte>(buffer, offset, length);
    }

    private void EnsureCapacity(int required)
    {
        byte[]? current = _buffer;
        if (current is not null && required <= current.Length)
            return;

        int doubled = current is null
            ? 256
            : current.Length > int.MaxValue / 2
                ? int.MaxValue
                : current.Length * 2;
        int requested = Math.Max(required, doubled);
        byte[] replacement = _pool.Rent(requested);
        try
        {
            _allocationChanged?.Invoke(replacement.LongLength);
        }
        catch
        {
            _pool.Return(replacement);
            throw;
        }

        if (current is not null)
            current.AsSpan(0, _length).CopyTo(replacement);
        _buffer = replacement;

        if (current is not null)
        {
            try
            {
                _allocationChanged?.Invoke(-current.LongLength);
            }
            finally
            {
                _pool.Return(current);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is null)
            return;

        try
        {
            _allocationChanged?.Invoke(-buffer.LongLength);
        }
        finally
        {
            _pool.Return(buffer);
            _length = 0;
        }
    }
}

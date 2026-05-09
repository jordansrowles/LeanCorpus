using System.Buffers;
using System.Runtime.CompilerServices;

namespace Rowles.LeanLucene.Store;

/// <summary>
/// Buffered sequential writer backed by <see cref="FileStream"/> and
/// <see cref="ArrayPool{T}"/>. Used at index-build time only.
/// </summary>
public sealed class IndexOutput : IDisposable
{
    private const int BufferSize = 65536;

    private readonly FileStream _stream;
    private readonly byte[] _buffer;
    private readonly bool _durable;
    private int _bufferPosition;
    private bool _disposed;

    /// <summary>Current logical write position (buffered + flushed).</summary>
    public long Position => _stream.Position + _bufferPosition;

    /// <summary>
    /// Creates a new file at <paramref name="filePath"/> and opens it for buffered sequential writing.
    /// </summary>
    /// <param name="filePath">The full path of the file to create.</param>
    /// <param name="durable">
    /// When <c>true</c>, <see cref="Dispose"/> calls <c>FileStream.Flush(flushToDisk: true)</c> so the
    /// file's bytes are persisted to the storage device before the handle is released. Defaults to
    /// <c>false</c> to preserve historic non-durable behaviour for callers that don't need it.
    /// </param>
    public IndexOutput(string filePath, bool durable = false)
    {
        _stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        _buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        _bufferPosition = 0;
        _durable = durable;
    }

    /// <summary>
    /// Seeks to an absolute position. Flushes the buffer first to ensure consistency.
    /// Use sparingly — this forces a buffer flush.
    /// </summary>
    public void Seek(long position)
    {
        FlushBuffer();
        _stream.Seek(position, SeekOrigin.Begin);
    }

    /// <summary>Writes the bytes from the given span to the output, flushing the internal buffer as needed.</summary>
    /// <param name="data">The bytes to write.</param>
    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            int remaining = BufferSize - _bufferPosition;
            int toCopy = Math.Min(remaining, data.Length - offset);
            data.Slice(offset, toCopy).CopyTo(_buffer.AsSpan(_bufferPosition, toCopy));
            _bufferPosition += toCopy;
            offset += toCopy;

            if (_bufferPosition == BufferSize)
                FlushBuffer();
        }
    }

    /// <summary>Writes all bytes from the given array to the output.</summary>
    /// <param name="data">The byte array to write.</param>
    public void WriteBytes(byte[] data) => WriteBytes(data.AsSpan());

    /// <summary>Writes a 32-bit signed integer in little-endian byte order.</summary>
    /// <param name="value">The integer to write.</param>
    public void WriteInt32(int value)
    {
        Span<byte> tmp = stackalloc byte[sizeof(int)];
        Unsafe.WriteUnaligned(ref tmp[0], value);
        WriteBytes(tmp);
    }

    /// <summary>Writes a 64-bit signed integer in little-endian byte order.</summary>
    /// <param name="value">The long integer to write.</param>
    public void WriteInt64(long value)
    {
        Span<byte> tmp = stackalloc byte[sizeof(long)];
        Unsafe.WriteUnaligned(ref tmp[0], value);
        WriteBytes(tmp);
    }

    /// <summary>Writes a 32-bit single-precision floating-point value.</summary>
    /// <param name="value">The float to write.</param>
    public void WriteSingle(float value)
    {
        Span<byte> tmp = stackalloc byte[sizeof(float)];
        Unsafe.WriteUnaligned(ref tmp[0], value);
        WriteBytes(tmp);
    }

    /// <summary>Writes a boolean as a single byte (0 or 1).</summary>
    /// <param name="value">The boolean value to write.</param>
    public void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    /// <summary>Writes a single byte to the output, flushing the buffer if it is full.</summary>
    /// <param name="value">The byte to write.</param>
    public void WriteByte(byte value)
    {
        if (_bufferPosition == BufferSize)
            FlushBuffer();

        _buffer[_bufferPosition++] = value;
    }

    /// <summary>
    /// Writes a non-negative integer using variable-length encoding (LEB128).
    /// Small values (0–127) consume a single byte. Encodes into a local buffer
    /// then writes all bytes in one call to reduce per-byte overhead.
    /// </summary>
    public void WriteVarInt(int value)
    {
        uint v = (uint)value;
        // Max 5 bytes for a 32-bit varint
        Span<byte> buf = stackalloc byte[5];
        int pos = 0;
        while (v >= 0x80)
        {
            buf[pos++] = (byte)(v | 0x80);
            v >>= 7;
        }
        buf[pos++] = (byte)v;
        WriteBytes(buf[..pos]);
    }

    /// <summary>Flushes the internal write buffer to the underlying file stream.</summary>
    public void Flush() => FlushBuffer();

    private void FlushBuffer()
    {
        if (_bufferPosition > 0)
        {
            _stream.Write(_buffer, 0, _bufferPosition);
            _bufferPosition = 0;
        }
    }

    /// <summary>Flushes remaining buffered data and releases all resources.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            FlushBuffer();
            if (_durable)
                _stream.Flush(flushToDisk: true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _stream.Dispose();
        }
    }
}

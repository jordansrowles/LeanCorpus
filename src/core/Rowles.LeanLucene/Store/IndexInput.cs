using System.Diagnostics.CodeAnalysis;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Rowles.LeanLucene.Store;

/// <summary>
/// Readable input over a memory-mapped file. Maintains a position cursor
/// and uses <see cref="Unsafe.ReadUnaligned{T}(ref readonly byte)"/> for primitive reads.
/// Acquired pointer is held for the lifetime of the accessor to avoid
/// repeated acquire/release overhead.
/// </summary>
public sealed unsafe class IndexInput : IDisposable
{
    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _accessor;
    private readonly long _length;
    private long _position;
    private bool _disposed;
    private byte* _ptr;

    /// <summary>
    /// Opens a file at <paramref name="filePath"/> as a memory-mapped read-only input.
    /// Acquires a native pointer for the lifetime of this instance.
    /// </summary>
    /// <param name="filePath">The full path of the file to open.</param>
    public IndexInput(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        _length = fileInfo.Length;

        if (_length == 0)
        {
            // Empty file — no data to map. Reads will throw EndOfStream naturally.
            _mmf = null;
            _accessor = null;
            _ptr = null;
            return;
        }

        _mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _accessor = _mmf.CreateViewAccessor(0, _length, MemoryMappedFileAccess.Read);
        _ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref _ptr);
        _ptr += _accessor.PointerOffset;
    }

    /// <summary>Total file length in bytes.</summary>
    public long Length => _length;

    /// <summary>Base pointer for the memory-mapped region. Used for zero-copy reads.</summary>
    internal byte* BasePointer => _ptr;

    /// <summary>Current read position within the file.</summary>
    public long Position => _position;

    /// <summary>Moves the read cursor to the specified absolute byte offset.</summary>
    /// <param name="position">The byte offset to seek to.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Seek(long position)
    {
        _position = position;
    }

    /// <summary>Reads and returns the next byte, advancing the position by one.</summary>
    /// <returns>The next byte in the stream.</returns>
    /// <exception cref="EndOfStreamException">Thrown if the end of the file has been reached.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte()
    {
        if (_position >= _length)
            ThrowEndOfStream();
        byte value = _ptr[_position];
        _position++;
        return value;
    }

    /// <summary>Reads and returns the next byte using a caller-supplied cursor, leaving <see cref="_position"/> untouched.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte(ref long position)
    {
        if (position >= _length)
            ThrowEndOfStream();
        byte value = _ptr[position];
        position++;
        return value;
    }

    /// <summary>Reads the next byte and returns <see langword="true"/> if it is non-zero.</summary>
    /// <returns><see langword="true"/> if the byte is non-zero; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBoolean()
    {
        return ReadByte() != 0;
    }

    /// <summary>Reads the next byte using a caller-supplied cursor and returns <see langword="true"/> if it is non-zero.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBoolean(ref long position) => ReadByte(ref position) != 0;

    /// <summary>Reads exactly <paramref name="count"/> bytes and returns them as a new array.</summary>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>A new byte array containing the read bytes.</returns>
    /// <exception cref="EndOfStreamException">Thrown if fewer than <paramref name="count"/> bytes remain.</exception>
    public byte[] ReadBytes(int count)
    {
        if (_position + count > _length)
            ThrowEndOfStream();
        var result = new byte[count];
        new ReadOnlySpan<byte>(_ptr + _position, count).CopyTo(result);
        _position += count;
        return result;
    }

    /// <summary>
    /// Returns a read-only span over the memory-mapped buffer at the current position
    /// without allocating. Advances the position by <paramref name="count"/> bytes.
    /// The span is only valid while the <see cref="IndexInput"/> is not disposed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ReadSpan(int count)
    {
        if (_position + count > _length)
            ThrowEndOfStream();
        var span = new ReadOnlySpan<byte>(_ptr + _position, count);
        _position += count;
        return span;
    }

    /// <summary>Stateless variant of <see cref="ReadSpan(int)"/> using a caller-supplied cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ReadSpan(int count, ref long position)
    {
        if (position + count > _length)
            ThrowEndOfStream();
        var span = new ReadOnlySpan<byte>(_ptr + position, count);
        position += count;
        return span;
    }

    /// <summary>Reads a 32-bit signed integer written in little-endian byte order.</summary>
    /// <returns>The decoded <see cref="int"/> value.</returns>
    /// <exception cref="EndOfStreamException">Thrown if fewer than 4 bytes remain.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32()
    {
        if (_position + sizeof(int) > _length)
            ThrowEndOfStream();
        int value = Unsafe.ReadUnaligned<int>(_ptr + _position);
        _position += sizeof(int);
        return value;
    }

    /// <summary>Stateless variant of <see cref="ReadInt32()"/> using a caller-supplied cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32(ref long position)
    {
        if (position + sizeof(int) > _length)
            ThrowEndOfStream();
        int value = Unsafe.ReadUnaligned<int>(_ptr + position);
        position += sizeof(int);
        return value;
    }

    /// <summary>
    /// Bulk-reads <paramref name="count"/> int32 values into the destination span.
    /// Single bounds check for the entire block. Much faster than N × ReadInt32().
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadInt32Array(Span<int> dest, int count)
    {
        int byteCount = count * sizeof(int);
        if (_position + byteCount > _length)
            ThrowEndOfStream();

        new ReadOnlySpan<byte>(_ptr + _position, byteCount)
            .CopyTo(MemoryMarshal.AsBytes(dest[..count]));
        _position += byteCount;
    }

    /// <summary>Stateless variant of <see cref="ReadInt32Array(Span{int},int)"/> using a caller-supplied cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadInt32Array(Span<int> dest, int count, ref long position)
    {
        int byteCount = count * sizeof(int);
        if (position + byteCount > _length)
            ThrowEndOfStream();

        new ReadOnlySpan<byte>(_ptr + position, byteCount)
            .CopyTo(MemoryMarshal.AsBytes(dest[..count]));
        position += byteCount;
    }

    /// <summary>Reads a 64-bit signed integer written in little-endian byte order.</summary>
    /// <returns>The decoded <see cref="long"/> value.</returns>
    /// <exception cref="EndOfStreamException">Thrown if fewer than 8 bytes remain.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadInt64()
    {
        if (_position + sizeof(long) > _length)
            ThrowEndOfStream();
        long value = Unsafe.ReadUnaligned<long>(_ptr + _position);
        _position += sizeof(long);
        return value;
    }

    /// <summary>Stateless variant of <see cref="ReadInt64()"/> using a caller-supplied cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadInt64(ref long position)
    {
        if (position + sizeof(long) > _length)
            ThrowEndOfStream();
        long value = Unsafe.ReadUnaligned<long>(_ptr + position);
        position += sizeof(long);
        return value;
    }

    /// <summary>Reads a 32-bit single-precision floating-point value.</summary>
    /// <returns>The decoded <see cref="float"/> value.</returns>
    /// <exception cref="EndOfStreamException">Thrown if fewer than 4 bytes remain.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadSingle()
    {
        if (_position + sizeof(float) > _length)
            ThrowEndOfStream();
        float value = Unsafe.ReadUnaligned<float>(_ptr + _position);
        _position += sizeof(float);
        return value;
    }

    /// <summary>Stateless variant of <see cref="ReadSingle()"/> using a caller-supplied cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadSingle(ref long position)
    {
        if (position + sizeof(float) > _length)
            ThrowEndOfStream();
        float value = Unsafe.ReadUnaligned<float>(_ptr + position);
        position += sizeof(float);
        return value;
    }

    /// <summary>Reads a 64-bit double-precision floating-point value.</summary>
    /// <returns>The decoded <see cref="double"/> value.</returns>
    /// <exception cref="EndOfStreamException">Thrown if fewer than 8 bytes remain.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadDouble()
    {
        if (_position + sizeof(double) > _length)
            ThrowEndOfStream();
        double value = Unsafe.ReadUnaligned<double>(_ptr + _position);
        _position += sizeof(double);
        return value;
    }

    /// <summary>
    /// Reads a length-prefixed UTF-8 string as written by <see cref="BinaryWriter.Write(string)"/>.
    /// The length prefix uses 7-bit encoded integer format.
    /// </summary>
    public string ReadLengthPrefixedString()
    {
        int byteLength = Read7BitEncodedInt();
        if (byteLength == 0) return string.Empty;
        if (_position + byteLength > _length)
            ThrowEndOfStream();
        var span = new ReadOnlySpan<byte>(_ptr + _position, byteLength);
        _position += byteLength;
        return System.Text.Encoding.UTF8.GetString(span);
    }

    /// <summary>
    /// Stateless variant of <see cref="ReadLengthPrefixedString()"/> using a caller-supplied cursor.
    /// </summary>
    public string ReadLengthPrefixedString(ref long position)
    {
        int byteLength = Read7BitEncodedInt(ref position);
        if (byteLength == 0) return string.Empty;
        if (position + byteLength > _length)
            ThrowEndOfStream();
        var span = new ReadOnlySpan<byte>(_ptr + position, byteLength);
        position += byteLength;
        return System.Text.Encoding.UTF8.GetString(span);
    }

    private int Read7BitEncodedInt()
    {
        int result = 0;
        int shift = 0;
        byte b;
        do
        {
            if (_position >= _length) ThrowEndOfStream();
            b = _ptr[_position++];
            result |= (b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return result;
    }

    private int Read7BitEncodedInt(ref long position)
    {
        int result = 0;
        int shift = 0;
        byte b;
        do
        {
            if (position >= _length) ThrowEndOfStream();
            b = _ptr[position++];
            result |= (b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return result;
    }

    /// <summary>
    /// Reads <paramref name="charCount"/> chars encoded as UTF-8 (as written by BinaryWriter.Write(char[])).
    /// Returns a newly allocated string. Used for one-time skip index loading.
    /// </summary>
    public string ReadUtf8String(int charCount)
    {
        byte* start = _ptr + _position;
        int remaining = (int)Math.Min(_length - _position, int.MaxValue);
        int byteCount = Utf8ByteCount(start, charCount, remaining);
        if (_position + byteCount > _length)
            ThrowEndOfStream();

        Span<char> buf = charCount <= 256 ? stackalloc char[charCount] : new char[charCount];
        System.Text.Encoding.UTF8.GetChars(new ReadOnlySpan<byte>(start, byteCount), buf);
        _position += byteCount;
        return new string(buf);
    }

    /// <summary>
    /// Compares <paramref name="charCount"/> UTF-8-encoded chars at the current position
    /// against <paramref name="termUtf8"/> raw UTF-8 bytes. Advances position past the bytes.
    /// Zero-allocation, no char decoding needed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareUtf8BytesAndAdvance(int charCount, ReadOnlySpan<byte> termUtf8)
    {
        byte* start = _ptr + _position;
        int remaining = (int)Math.Min(_length - _position, int.MaxValue);
        int byteCount = Utf8ByteCount(start, charCount, remaining);
        if (_position + byteCount > _length)
            ThrowEndOfStream();

        var fileBytes = new ReadOnlySpan<byte>(start, byteCount);
        _position += byteCount;
        return fileBytes.SequenceCompareTo(termUtf8);
    }

    /// <summary>
    /// Compares <paramref name="charCount"/> UTF-8-encoded chars at the current position
    /// against <paramref name="term"/> using ordinal comparison. Advances position past the bytes.
    /// Zero-allocation (stackalloc for decode buffer).
    /// </summary>
    public int CompareCharsAndAdvance(int charCount, ReadOnlySpan<char> term)
    {
        byte* start = _ptr + _position;
        int remaining = (int)Math.Min(_length - _position, int.MaxValue);
        int byteCount = Utf8ByteCount(start, charCount, remaining);
        if (_position + byteCount > _length)
            ThrowEndOfStream();

        Span<char> buf = charCount <= 256 ? stackalloc char[charCount] : new char[charCount];
        System.Text.Encoding.UTF8.GetChars(new ReadOnlySpan<byte>(start, byteCount), buf);
        _position += byteCount;
        return buf.SequenceCompareTo(term);
    }

    /// <summary>
    /// Counts the number of UTF-8 bytes needed to encode <paramref name="charCount"/> characters.
    /// <paramref name="maxBytes"/> limits how far we read to prevent out-of-bounds access on
    /// truncated or malformed data.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Utf8ByteCount(byte* p, int charCount, int maxBytes)
    {
        // Fast path: check if the first charCount bytes are all ASCII.
        // Guard against reading beyond the mapped region.
        if (charCount <= maxBytes)
        {
            bool allAscii = true;
            for (int i = 0; i < charCount; i++)
            {
                if (p[i] >= 0x80) { allAscii = false; break; }
            }
            if (allAscii) return charCount;
        }

        // Slow path: variable-width UTF-8 with bounds enforcement.
        int bytes = 0;
        int chars = 0;
        while (chars < charCount)
        {
            if (bytes >= maxBytes)
                ThrowCorruptUtf8();

            byte b = p[bytes];
            int seqLen;
            int charLen;
            if (b < 0x80) { seqLen = 1; charLen = 1; }
            else if ((b & 0xE0) == 0xC0) { seqLen = 2; charLen = 1; }
            else if ((b & 0xF0) == 0xE0) { seqLen = 3; charLen = 1; }
            else { seqLen = 4; charLen = 2; }

            if (bytes + seqLen > maxBytes)
                ThrowCorruptUtf8();

            bytes += seqLen;
            chars += charLen;
        }
        return bytes;
    }

    [DoesNotReturn]
    private static void ThrowCorruptUtf8()
        => throw new InvalidDataException("Truncated or malformed UTF-8 data in index file.");

    /// <summary>
    /// Reads a variable-length encoded non-negative integer (LEB128).
    /// Small values (0–127) consume a single byte.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadVarInt()
    {
        uint result = 0;
        int shift = 0;
        byte b;
        do
        {
            if (_position >= _length)
                ThrowEndOfStream();
            b = _ptr[_position++];
            if (shift >= 35)
                throw new InvalidDataException("VarInt is too large or malformed.");
            result |= (uint)(b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);

        if (result > int.MaxValue)
            throw new InvalidDataException("VarInt exceeds Int32 range.");
        return (int)result;
    }

    /// <summary>Stateless variant of <see cref="ReadVarInt()"/> using a caller-supplied cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadVarInt(ref long position)
    {
        uint result = 0;
        int shift = 0;
        byte b;
        do
        {
            if (position >= _length)
                ThrowEndOfStream();
            b = _ptr[position++];
            if (shift >= 35)
                throw new InvalidDataException("VarInt is too large or malformed.");
            result |= (uint)(b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);

        if (result > int.MaxValue)
            throw new InvalidDataException("VarInt exceeds Int32 range.");
        return (int)result;
    }

    /// <summary>
    /// Unrolled VarInt decoder with a single per-value bounds check. If at least 5 bytes
    /// remain, uses the branchless unrolled path. Otherwise falls back to the safe
    /// per-byte checked path. This eliminates up to 4 bounds checks per VarInt value
    /// compared to <see cref="ReadVarInt()"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int ReadVarIntFast()
    {
        if (_position + 5 <= _length)
        {
            byte* p = _ptr + _position;
            uint result = (uint)(p[0] & 0x7F);
            if (p[0] < 0x80) { _position += 1; return (int)result; }
            result |= (uint)(p[1] & 0x7F) << 7;
            if (p[1] < 0x80) { _position += 2; return (int)result; }
            result |= (uint)(p[2] & 0x7F) << 14;
            if (p[2] < 0x80) { _position += 3; return (int)result; }
            result |= (uint)(p[3] & 0x7F) << 21;
            if (p[3] < 0x80) { _position += 4; return (int)result; }
            result |= (uint)(p[4] & 0x7F) << 28;
            _position += 5;
            return (int)result;
        }
        return ReadVarInt();
    }

    /// <summary>Stateless variant of <see cref="ReadVarIntFast()"/> using a caller-supplied cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int ReadVarIntFast(ref long position)
    {
        if (position + 5 <= _length)
        {
            byte* p = _ptr + position;
            uint result = (uint)(p[0] & 0x7F);
            if (p[0] < 0x80) { position += 1; return (int)result; }
            result |= (uint)(p[1] & 0x7F) << 7;
            if (p[1] < 0x80) { position += 2; return (int)result; }
            result |= (uint)(p[2] & 0x7F) << 14;
            if (p[2] < 0x80) { position += 3; return (int)result; }
            result |= (uint)(p[3] & 0x7F) << 21;
            if (p[3] < 0x80) { position += 4; return (int)result; }
            result |= (uint)(p[4] & 0x7F) << 28;
            position += 5;
            return (int)result;
        }
        return ReadVarInt(ref position);
    }

    /// <summary>
    /// Hints the OS to prefetch the mapped region for sequential access.
    /// Uses PrefetchVirtualMemory on Windows and madvise(MADV_SEQUENTIAL) on Linux.
    /// Failures are silently ignored (advisory only).
    /// </summary>
    public void Prefetch()
    {
        if (_length == 0 || _ptr == null) return;

        if (OperatingSystem.IsWindows())
            PrefetchWindows();
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            PrefetchPosix();
    }

    private void PrefetchWindows()
    {
        var handle = NativeMethods.GetCurrentProcess();
        if (handle == IntPtr.Zero) return;

        var entry = new NativeMethods.WIN32_MEMORY_RANGE_ENTRY
        {
            VirtualAddress = (nint)_ptr,
            NumberOfBytes = (nuint)_length
        };
        NativeMethods.PrefetchVirtualMemory(handle, 1, &entry, 0);
    }

    private void PrefetchPosix()
    {
        // MADV_SEQUENTIAL = 2 on Linux and macOS
        NativeMethods.madvise((nint)_ptr, (nuint)_length, 2);
    }

    /// <summary>Releases the memory-mapped file view and underlying file resources.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_accessor is not null)
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _ptr = null;
            _accessor.Dispose();
        }
        _mmf?.Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowEndOfStream()
        => throw new EndOfStreamException("Attempted to read beyond the end of the mapped file.");
}

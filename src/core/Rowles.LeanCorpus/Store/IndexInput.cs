using System.Diagnostics.CodeAnalysis;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Rowles.LeanCorpus.Store;

/// <summary>
/// Readable input over a memory-mapped file. Maintains a position cursor
/// and uses <see cref="Unsafe.ReadUnaligned{T}(ref readonly byte)"/> for primitive reads.
/// Acquired pointer is held for the lifetime of the accessor to avoid
/// repeated acquire/release overhead.
/// </summary>
public sealed unsafe class IndexInput : IDisposable
{
    /// <summary>Full path of the file backing this input. Internal for deferred-deletion tracking.</summary>
    internal string? FilePath => _filePath;

    /// <summary>
    /// Registers a callback invoked when this input is disposed, after all memory-mapped
    /// resources are released. Used by <see cref="MMapDirectory"/> for reference-counted
    /// deferred file deletion.
    /// </summary>
    internal void SetOnDisposed(Action<IndexInput> callback) => _onDisposed = callback;

    private readonly string? _filePath;
    private readonly long _fileOffset;
    private readonly OperationDrain _operations = new();
    private Action<IndexInput>? _onDisposed;
    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _accessor;
    private readonly IndexInputLifetimeLease? _ownerLease;
    private readonly IndexInput? _sharedMappingOwner;
    private readonly object _mappingIdentity;
    private readonly long _length;
    private long _position;
    private bool _disposed;
    private int _disposeStarted;
    private byte* _ptr;

    /// <summary>
    /// Opens a file at <paramref name="filePath"/> as a memory-mapped read-only input.
    /// Acquires a native pointer for the lifetime of this instance.
    /// </summary>
    /// <param name="filePath">The full path of the file to open.</param>
    public IndexInput(string filePath)
        : this(filePath, 0, length: null)
    {
    }

    /// <summary>
    /// Opens a bounded byte range from a file as a memory-mapped input. The physical file
    /// path is retained for lifetime tracking while positions remain relative to the range.
    /// </summary>
    /// <param name="filePath">The full path of the file to open.</param>
    /// <param name="offset">The first byte in the file included in the input.</param>
    /// <param name="length">The number of bytes included in the input.</param>
    internal IndexInput(string filePath, long offset, long? length)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        _filePath = filePath;
        _fileOffset = offset;
        _mappingIdentity = new object();
        Diagnostics.FileSystemDiagnostics.RecordIndexInputOpen();
        long fileLength = new FileInfo(filePath).Length;
        if (offset > fileLength)
            throw new ArgumentOutOfRangeException(nameof(offset), "The input offset is outside the file.");

        _length = length ?? fileLength - offset;
        if (_length < 0 || _length > fileLength - offset)
            throw new ArgumentOutOfRangeException(nameof(length), "The input range is outside the file.");

        if (_length == 0)
        {
            // Empty file — no data to map. Reads will throw EndOfStream naturally.
            _mmf = null;
            _accessor = null;
            _ptr = null;
            return;
        }

        // Open with FileShare.Delete so Windows allows File.Delete() while the mapping is
        // still active. Without this, the merge's CleanupSegmentFiles cannot delete old
        // segment files that are still mapped by a live IndexSearcher, causing orphan
        // files to accumulate on disk and fsync storms that stall the writer.
        var fs = (FileStream)FileOpenRetry.OpenReadDelete(filePath);
        _mmf = MemoryMappedFile.CreateFromFile(fs, null, 0,
            MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
        Diagnostics.FileSystemDiagnostics.RecordMemoryMappedFileCreation();
        const long allocationGranularity = 64 * 1024;
        long viewOffset = offset - offset % allocationGranularity;
        long pointerDelta = offset - viewOffset;
        _accessor = _mmf.CreateViewAccessor(viewOffset, checked(pointerDelta + _length), MemoryMappedFileAccess.Read);
        Diagnostics.FileSystemDiagnostics.RecordMemoryMappedViewCreation();
        _ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref _ptr);
        _ptr += _accessor.PointerOffset + pointerDelta;
    }

    private IndexInput(
        IndexInput owner,
        IndexInput sharedMappingOwner,
        long offset,
        long length,
        IndexInputLifetimeLease ownerLease)
    {
        _filePath = owner._filePath;
        _fileOffset = checked(owner._fileOffset + offset);
        _length = length;
        _ptr = owner._ptr + offset;
        _ownerLease = ownerLease;
        _sharedMappingOwner = sharedMappingOwner;
        _mappingIdentity = owner._mappingIdentity;
        Diagnostics.FileSystemDiagnostics.RecordIndexInputOpen();
    }

    internal object MappingIdentity => _mappingIdentity;

    /// <summary>Total input length in bytes.</summary>
    public long Length => _length;

    /// <summary>Opens a separately owned input bounded to a range within this input.</summary>
    internal IndexInput OpenSlice(long offset, long length)
    {
        using var operation = EnterReadScope();
        if (offset < 0 || length < 0 || offset > _length || length > _length - offset)
            throw new ArgumentOutOfRangeException(nameof(offset), "The input slice is outside the current input range.");

        if (_sharedMappingOwner is not null)
            return OpenSharedSliceCore(offset, length);
        return new IndexInput(_filePath!, checked(_fileOffset + offset), length);
    }

    /// <summary>
    /// Opens a bounded child view over this mapping. The owner must enclose every child
    /// lifetime because disposing it waits synchronously for the child views to drain.
    /// </summary>
    internal IndexInput OpenSharedSlice(long offset, long length)
    {
        using var operation = EnterReadScope();
        if (offset < 0 || length < 0 || offset > _length || length > _length - offset)
            throw new ArgumentOutOfRangeException(nameof(offset), "The input slice is outside the current input range.");

        return OpenSharedSliceCore(offset, length);
    }

    private IndexInput OpenSharedSliceCore(long offset, long length)
    {
        IndexInput mappingOwner = _sharedMappingOwner ?? this;
        var ownerLease = mappingOwner.AcquireLifetimeLease();
        try
        {
            return new IndexInput(this, mappingOwner, offset, length, ownerLease);
        }
        catch
        {
            ownerLease.Dispose();
            throw;
        }
    }

    /// <summary>Base pointer for the memory-mapped region. Used for zero-copy reads.</summary>
    internal byte* BasePointer
    {
        get
        {
            ThrowIfDisposed();
            return _ptr;
        }
    }

    /// <summary>Current read position within the file.</summary>
    public long Position => _position;

    /// <summary>Moves the read cursor to the specified absolute byte offset.</summary>
    /// <param name="position">The byte offset to seek to.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Seek(long position)
    {
        using var operation = EnterReadScope();
        if (position < 0 || position > _length)
            ThrowInvalidSeekPosition();
        _position = position;
    }

    /// <summary>Reads and returns the next byte, advancing the position by one.</summary>
    /// <returns>The next byte in the stream.</returns>
    /// <exception cref="EndOfStreamException">Thrown if the end of the file has been reached.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte()
    {
        using var operation = EnterReadScope();
        EnsureAvailable(_position, sizeof(byte));
        byte value = _ptr[_position];
        _position++;
        return value;
    }

    /// <summary>Reads and returns the next byte using a caller-supplied cursor, leaving <see cref="_position"/> untouched.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte(ref long position)
    {
        using var operation = EnterReadScope();
        EnsureAvailable(position, sizeof(byte));
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
        using var operation = EnterReadScope();
        EnsureAvailable(_position, count);
        var result = new byte[count];
        new ReadOnlySpan<byte>(_ptr + _position, count).CopyTo(result);
        _position += count;
        return result;
    }

    /// <summary>Copies bytes into a caller-owned buffer without exposing mapped memory.</summary>
    internal void ReadBytes(Span<byte> destination)
    {
        using var operation = EnterReadScope();
        EnsureAvailable(_position, destination.Length);
        new ReadOnlySpan<byte>(_ptr + _position, destination.Length).CopyTo(destination);
        _position += destination.Length;
    }

    /// <summary>
    /// Returns a stable read-only span containing bytes from the current position.
    /// Advances the position by <paramref name="count"/> bytes. The returned data
    /// remains valid after this input is disposed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ReadSpan(int count)
    {
        using var operation = EnterReadScope();
        EnsureAvailable(_position, count);
        var bytes = new byte[count];
        new ReadOnlySpan<byte>(_ptr + _position, count).CopyTo(bytes);
        _position += count;
        return bytes;
    }

    /// <summary>Stateless variant of <see cref="ReadSpan(int)"/> using a caller-supplied cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ReadSpan(int count, scoped ref long position)
    {
        using var operation = EnterReadScope();
        EnsureAvailable(position, count);
        var bytes = new byte[count];
        new ReadOnlySpan<byte>(_ptr + position, count).CopyTo(bytes);
        position += count;
        return bytes;
    }

    /// <summary>
    /// Borrows a zero-copy span while the caller owns a containing input, segment or
    /// query lifetime lease. The span must not escape that operation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ReadOnlySpan<byte> BorrowSpan(int count)
    {
        EnsureAvailable(_position, count);
        var span = new ReadOnlySpan<byte>(_ptr + _position, count);
        _position += count;
        return span;
    }

    /// <summary>Stateless zero-copy borrowed-span variant.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ReadOnlySpan<byte> BorrowSpan(int count, scoped ref long position)
    {
        EnsureAvailable(position, count);
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
        using var operation = EnterReadScope();
        EnsureAvailable(_position, sizeof(int));
        int value = Unsafe.ReadUnaligned<int>(_ptr + _position);
        _position += sizeof(int);
        return value;
    }

    /// <summary>Stateless variant of <see cref="ReadInt32()"/> using a caller-supplied cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32(ref long position)
    {
        using var operation = EnterReadScope();
        EnsureAvailable(position, sizeof(int));
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
        using var operation = EnterReadScope();
        int byteCount = EnsureArrayAvailable(_position, count, dest.Length, sizeof(int));

        new ReadOnlySpan<byte>(_ptr + _position, byteCount)
            .CopyTo(MemoryMarshal.AsBytes(dest[..count]));
        _position += byteCount;
    }

    /// <summary>Stateless variant of <see cref="ReadInt32Array(Span{int},int)"/> using a caller-supplied cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadInt32Array(Span<int> dest, int count, ref long position)
    {
        using var operation = EnterReadScope();
        int byteCount = EnsureArrayAvailable(position, count, dest.Length, sizeof(int));

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
        using var operation = EnterReadScope();
        EnsureAvailable(_position, sizeof(long));
        long value = Unsafe.ReadUnaligned<long>(_ptr + _position);
        _position += sizeof(long);
        return value;
    }

    /// <summary>Stateless variant of <see cref="ReadInt64()"/> using a caller-supplied cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadInt64(ref long position)
    {
        using var operation = EnterReadScope();
        EnsureAvailable(position, sizeof(long));
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
        using var operation = EnterReadScope();
        EnsureAvailable(_position, sizeof(float));
        float value = Unsafe.ReadUnaligned<float>(_ptr + _position);
        _position += sizeof(float);
        return value;
    }

    /// <summary>Stateless variant of <see cref="ReadSingle()"/> using a caller-supplied cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadSingle(ref long position)
    {
        using var operation = EnterReadScope();
        EnsureAvailable(position, sizeof(float));
        float value = Unsafe.ReadUnaligned<float>(_ptr + position);
        position += sizeof(float);
        return value;
    }

    /// <summary>Bulk-reads single-precision values using a caller-supplied cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadSingleArray(Span<float> destination, int count, ref long position)
    {
        using var operation = EnterReadScope();
        int byteCount = EnsureArrayAvailable(position, count, destination.Length, sizeof(float));

        new ReadOnlySpan<byte>(_ptr + position, byteCount)
            .CopyTo(MemoryMarshal.AsBytes(destination[..count]));
        position += byteCount;
    }

    /// <summary>Reads a 64-bit double-precision floating-point value.</summary>
    /// <returns>The decoded <see cref="double"/> value.</returns>
    /// <exception cref="EndOfStreamException">Thrown if fewer than 8 bytes remain.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadDouble()
    {
        using var operation = EnterReadScope();
        EnsureAvailable(_position, sizeof(double));
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
        using var operation = EnterReadScope();
        int byteLength = Read7BitEncodedInt();
        if (byteLength == 0) return string.Empty;
        EnsureAvailable(_position, byteLength);
        var span = new ReadOnlySpan<byte>(_ptr + _position, byteLength);
        _position += byteLength;
        return System.Text.Encoding.UTF8.GetString(span);
    }

    /// <summary>
    /// Stateless variant of <see cref="ReadLengthPrefixedString()"/> using a caller-supplied cursor.
    /// </summary>
    public string ReadLengthPrefixedString(ref long position)
    {
        using var operation = EnterReadScope();
        int byteLength = Read7BitEncodedInt(ref position);
        if (byteLength == 0) return string.Empty;
        EnsureAvailable(position, byteLength);
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
            EnsureAvailable(_position, sizeof(byte));
            b = _ptr[_position++];
            if (shift >= 35)
                throw new InvalidDataException("7-bit encoded integer is too large or malformed.");
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
            EnsureAvailable(position, sizeof(byte));
            b = _ptr[position++];
            if (shift >= 35)
                throw new InvalidDataException("7-bit encoded integer is too large or malformed.");
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
        using var operation = EnterReadScope();
        EnsureCursor(_position);
        if (charCount < 0)
            ThrowNegativeCount();
        byte* start = _ptr + _position;
        int remaining = (int)Math.Min(_length - _position, int.MaxValue);
        int byteCount = Utf8ByteCount(start, charCount, remaining);
        EnsureAvailable(_position, byteCount);

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
        using var operation = EnterReadScope();
        EnsureCursor(_position);
        if (charCount < 0)
            ThrowNegativeCount();
        byte* start = _ptr + _position;
        int remaining = (int)Math.Min(_length - _position, int.MaxValue);
        int byteCount = Utf8ByteCount(start, charCount, remaining);
        EnsureAvailable(_position, byteCount);

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
        using var operation = EnterReadScope();
        EnsureCursor(_position);
        if (charCount < 0)
            ThrowNegativeCount();
        byte* start = _ptr + _position;
        int remaining = (int)Math.Min(_length - _position, int.MaxValue);
        int byteCount = Utf8ByteCount(start, charCount, remaining);
        EnsureAvailable(_position, byteCount);

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

            if (seqLen > maxBytes - bytes)
                ThrowCorruptUtf8();

            bytes += seqLen;
            if (charLen > charCount - chars)
                ThrowCorruptUtf8();
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
        using var operation = EnterReadScope();
        return ReadVarIntCore(ref _position);
    }

    /// <summary>Stateless variant of <see cref="ReadVarInt()"/> using a caller-supplied cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadVarInt(ref long position)
    {
        using var operation = EnterReadScope();
        return ReadVarIntCore(ref position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadVarIntCore(ref long position)
    {
        uint result = 0;
        int shift = 0;
        byte b;
        do
        {
            EnsureAvailable(position, sizeof(byte));
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
        using var operation = EnterReadScope();
        return ReadVarIntFastCore(ref _position);
    }

    /// <summary>Stateless variant of <see cref="ReadVarIntFast()"/> using a caller-supplied cursor.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int ReadVarIntFast(ref long position)
    {
        using var operation = EnterReadScope();
        return ReadVarIntFastCore(ref position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadVarIntFastCore(ref long position)
    {
        EnsureCursor(position);
        if (position <= _length - 5)
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
        return ReadVarIntCore(ref position);
    }

    /// <summary>
    /// Acquires one operation scope for a decoder that performs several primitive reads.
    /// The session must remain local to the decoding operation and must not escape it.
    /// </summary>
    internal ReadSession BeginReadSession() => new(this);

    /// <summary>
    /// Hints the OS to prefetch the mapped region for sequential access.
    /// Uses PrefetchVirtualMemory on Windows and madvise(MADV_SEQUENTIAL) on Linux.
    /// Failures are silently ignored (advisory only).
    /// </summary>
    public void Prefetch()
    {
        using var operation = EnterReadScope();
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
        if (Interlocked.CompareExchange(ref _disposeStarted, 1, 0) != 0)
            return;

        _disposed = true;
        _operations.BeginDisposeAndWait();
        ReleaseResources(notifyDirectory: true);
        GC.SuppressFinalize(this);
    }

    private void ReleaseResources(bool notifyDirectory)
    {
        if (_accessor is not null)
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _ptr = null;
            _accessor.Dispose();
        }
        _mmf?.Dispose();
        _ownerLease?.Dispose();

        if (notifyDirectory)
        {
            // Notify the directory that this input's file mapping is released so
            // any pending-delete file can now be removed.
            _onDisposed?.Invoke(this);
        }
    }

    /// <summary>
    /// Finaliser that releases the native memory-mapped view pointer if <see cref="Dispose"/>
    /// was not called. This is a safety net; callers should always dispose explicitly.
    /// </summary>
    ~IndexInput()
    {
        if (Interlocked.CompareExchange(ref _disposeStarted, 1, 0) != 0)
            return;

        _disposed = true;
        ReleaseResources(notifyDirectory: false);
    }

    private ReadScope EnterReadScope() => new(this);

    internal IndexInputLifetimeLease AcquireLifetimeLease()
        => new(this, _operations.Acquire(this));

    private struct ReadScope : IDisposable
    {
        private OperationDrain.Scope _inputScope;

        internal ReadScope(IndexInput input) => _inputScope = input._operations.Enter(input);

        public void Dispose() => _inputScope.Dispose();
    }

    /// <summary>Scoped primitive reader backed by one input operation lease.</summary>
    internal ref struct ReadSession
    {
        private readonly IndexInput _input;
        private OperationDrain.Scope _scope;

        internal ReadSession(IndexInput input)
        {
            _input = input;
            _scope = input._operations.Enter(input);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal byte ReadByte(ref long position)
        {
            _input.EnsureAvailable(position, sizeof(byte));
            return _input._ptr[position++];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int ReadInt32(ref long position)
        {
            _input.EnsureAvailable(position, sizeof(int));
            int value = Unsafe.ReadUnaligned<int>(_input._ptr + position);
            position += sizeof(int);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal long ReadInt64(ref long position)
        {
            _input.EnsureAvailable(position, sizeof(long));
            long value = Unsafe.ReadUnaligned<long>(_input._ptr + position);
            position += sizeof(long);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal float ReadSingle(ref long position)
        {
            _input.EnsureAvailable(position, sizeof(float));
            float value = Unsafe.ReadUnaligned<float>(_input._ptr + position);
            position += sizeof(float);
            return value;
        }

        internal string ReadLengthPrefixedString(ref long position)
        {
            int byteLength = _input.Read7BitEncodedInt(ref position);
            if (byteLength == 0)
                return string.Empty;
            _input.EnsureAvailable(position, byteLength);
            var span = new ReadOnlySpan<byte>(_input._ptr + position, byteLength);
            position += byteLength;
            return System.Text.Encoding.UTF8.GetString(span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int ReadVarInt(ref long position) => _input.ReadVarIntCore(ref position);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int ReadVarIntFast(ref long position) => _input.ReadVarIntFastCore(ref position);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<byte> BorrowSpan(int count, ref long position)
            => _input.BorrowSpan(count, ref position);

        public void Dispose() => _scope.Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowEndOfStream()
        => throw new EndOfStreamException("Attempted to read beyond the end of the mapped file.");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCursor(long position)
    {
        ThrowIfDisposed();
        if (position < 0)
            ThrowNegativePosition();
        if (position > _length)
            ThrowEndOfStream();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureAvailable(long position, long count)
    {
        ThrowIfDisposed();
        if (position < 0)
            ThrowNegativePosition();
        if (count < 0)
            ThrowNegativeCount();
        if (position > _length || count > _length - position)
            ThrowEndOfStream();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int EnsureArrayAvailable(long position, int count, int destinationLength, int elementSize)
    {
        long byteCount = (long)count * elementSize;
        EnsureAvailable(position, byteCount);
        if (count > destinationLength)
            ThrowDestinationTooSmall();
        if (byteCount > int.MaxValue)
            ThrowArrayTooLarge();
        return (int)byteCount;
    }

    [DoesNotReturn]
    private static void ThrowInvalidSeekPosition()
        => throw new ArgumentOutOfRangeException("position", "Position must be within the mapped input.");

    [DoesNotReturn]
    private static void ThrowNegativePosition()
        => throw new ArgumentOutOfRangeException("position", "Position must be non-negative.");

    [DoesNotReturn]
    private static void ThrowNegativeCount()
        => throw new ArgumentOutOfRangeException("count", "Count must be non-negative.");

    [DoesNotReturn]
    private static void ThrowDestinationTooSmall()
        => throw new ArgumentOutOfRangeException("count", "Count exceeds the destination span length.");

    [DoesNotReturn]
    private static void ThrowArrayTooLarge()
        => throw new ArgumentOutOfRangeException("count", "The requested array is too large for a byte span.");
}

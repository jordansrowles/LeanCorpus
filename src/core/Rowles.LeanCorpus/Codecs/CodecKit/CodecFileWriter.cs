using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>Writes positively identified canonical codec-file frames.</summary>
public static class CodecFileWriter
{
    internal const uint Magic = 0x4643_434c;
    internal const byte CurrentFrameVersion = 1;
    internal const int FixedHeaderLength = 16;
    internal const int FooterLength = 16;
    internal const int MaximumFormatIdBytes = 64;

    /// <summary>Begins a current canonical frame from its authoritative catalogue descriptor.</summary>
    public static CodecWriteSession Begin(IndexOutput output, CodecFileDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.CurrentFraming != CodecFramingPolicy.Canonical || !descriptor.CurrentFormatVersion.HasValue)
            throw new ArgumentException($"Codec format '{descriptor.FormatId}' is not a versioned canonical format.", nameof(descriptor));

        return Begin(
            output,
            descriptor.FormatId,
            descriptor.CurrentFormatVersion.Value,
            checksumAlgorithm: ToFrameChecksum(descriptor.ChecksumPolicy));
    }

    /// <summary>Writes and atomically publishes a descriptor's current canonical format.</summary>
    public static void WriteAtomically(
        string path,
        CodecFileDescriptor descriptor,
        bool durable,
        Action<CodecBodyOutput> writeBody,
        bool dropPageCache = false)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.CurrentFraming != CodecFramingPolicy.Canonical || !descriptor.CurrentFormatVersion.HasValue)
            throw new ArgumentException($"Codec format '{descriptor.FormatId}' is not a versioned canonical format.", nameof(descriptor));

        WriteAtomically(
            path,
            descriptor.FormatId,
            descriptor.CurrentFormatVersion.Value,
            durable,
            writeBody,
            checksumAlgorithm: ToFrameChecksum(descriptor.ChecksumPolicy),
            dropPageCache: dropPageCache);
    }

    /// <summary>Writes and atomically publishes one complete canonical codec file.</summary>
    public static void WriteAtomically(
        string path,
        string formatId,
        int formatVersion,
        bool durable,
        Action<CodecBodyOutput> writeBody,
        CodecFrameFlags flags = CodecFrameFlags.None,
        CodecFileChecksumAlgorithm checksumAlgorithm = CodecFileChecksumAlgorithm.XxHash64,
        bool dropPageCache = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writeBody);

        string temporaryPath = string.Concat(path, ".", Guid.NewGuid().ToString("N"), ".codec.tmp");
        try
        {
            using (var output = new IndexOutput(temporaryPath, durable, dropPageCache))
            using (var session = Begin(output, formatId, formatVersion, flags, checksumAlgorithm))
            {
                writeBody(session.Output);
                session.Complete();
            }

            FileOpenRetry.Move(temporaryPath, path, overwrite: true);
            if (durable)
                DirectoryFsync.Sync(Path.GetDirectoryName(path) ?? string.Empty, strict: true);
        }
        catch
        {
            try
            {
                FileOpenRetry.Delete(temporaryPath);
            }
            catch (Exception ex)
            {
                Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "canonical codec-file temporary cleanup");
            }
            throw;
        }
    }

    /// <summary>Begins a streaming canonical frame.</summary>
    public static CodecWriteSession Begin(
        IndexOutput output,
        string formatId,
        int formatVersion,
        CodecFrameFlags flags = CodecFrameFlags.None,
        CodecFileChecksumAlgorithm checksumAlgorithm = CodecFileChecksumAlgorithm.XxHash64)
    {
        ArgumentNullException.ThrowIfNull(output);
        byte[] identifier = EncodeAndValidateFormatId(formatId);
        if (formatVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(formatVersion), formatVersion, "Format version must be positive.");
        if (flags != CodecFrameFlags.None)
            throw new ArgumentOutOfRangeException(nameof(flags), flags, "Frame v1 does not define any flags.");
        ValidateChecksumAlgorithm(checksumAlgorithm);

        output.WriteInt32(unchecked((int)Magic));
        output.WriteByte(CurrentFrameVersion);
        output.WriteByte(checked((byte)identifier.Length));
        output.WriteInt32(formatVersion);
        output.WriteInt32(unchecked((int)flags));
        output.WriteByte((byte)checksumAlgorithm);
        output.WriteByte(0);
        output.WriteBytes(identifier);

        return new CodecWriteSession(output, formatId, formatVersion, flags, checksumAlgorithm);
    }

    internal static byte[] EncodeAndValidateFormatId(string formatId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatId);
        if (formatId.Length > MaximumFormatIdBytes)
            throw new ArgumentException($"Format identifier exceeds {MaximumFormatIdBytes} bytes.", nameof(formatId));

        var segments = formatId.Split('.');
        if (segments.Length < 2)
            throw new ArgumentException("Format identifier must be namespaced with at least two dot-separated segments.", nameof(formatId));

        foreach (string segment in segments)
        {
            if (segment.Length == 0 || segment[0] is < 'a' or > 'z')
                throw new ArgumentException("Each format identifier segment must start with a lowercase ASCII letter.", nameof(formatId));
            foreach (char value in segment)
            {
                if (value is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-')
                    throw new ArgumentException("Format identifier segments may contain only lowercase ASCII letters, digits, and '-'.", nameof(formatId));
            }
        }

        return Encoding.ASCII.GetBytes(formatId);
    }

    internal static void ValidateChecksumAlgorithm(CodecFileChecksumAlgorithm checksumAlgorithm)
    {
        if (checksumAlgorithm is < CodecFileChecksumAlgorithm.None or > CodecFileChecksumAlgorithm.XxHash64)
            throw new ArgumentOutOfRangeException(nameof(checksumAlgorithm), checksumAlgorithm, "Unknown codec-file checksum algorithm.");
    }

    private static CodecFileChecksumAlgorithm ToFrameChecksum(CodecChecksumPolicy policy)
        => policy switch
        {
            CodecChecksumPolicy.None => CodecFileChecksumAlgorithm.None,
            CodecChecksumPolicy.XxHash64 => CodecFileChecksumAlgorithm.XxHash64,
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown catalogue checksum policy."),
        };
}

/// <summary>A canonical frame write that becomes valid only after <see cref="Complete"/>.</summary>
public sealed class CodecWriteSession : IDisposable
{
    private readonly IndexOutput _output;
    private readonly long _bodyStart;
    private bool _completed;
    private bool _disposed;

    internal CodecWriteSession(
        IndexOutput output,
        string formatId,
        int formatVersion,
        CodecFrameFlags flags,
        CodecFileChecksumAlgorithm checksumAlgorithm)
    {
        _output = output;
        _bodyStart = output.Position;
        Metadata = new CodecFrameMetadata(formatId, CodecFileWriter.CurrentFrameVersion, formatVersion, flags, checksumAlgorithm, _bodyStart, 0, 0);
        Output = new CodecBodyOutput(output, CreateAccumulator(checksumAlgorithm), () => _completed || _disposed);
    }

    public CodecFrameMetadata Metadata { get; private set; }

    public CodecBodyOutput Output { get; }

    /// <summary>Finalises the body checksum and fixed footer.</summary>
    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
            throw new InvalidOperationException("The codec write session has already been completed.");

        long bodyLength = _output.Position - _bodyStart;
        ulong checksum = Output.GetChecksum();
        _output.WriteInt64(bodyLength);
        Span<byte> checksumBytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(checksumBytes, checksum);
        _output.WriteBytes(checksumBytes);
        _completed = true;
        Metadata = Metadata with { BodyLength = bodyLength, StoredChecksum = checksum };
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Output.Dispose();
    }

    internal static IFileChecksumAccumulator CreateAccumulator(CodecFileChecksumAlgorithm algorithm)
        => algorithm switch
        {
            CodecFileChecksumAlgorithm.None => new NoFileChecksumAccumulator(),
            CodecFileChecksumAlgorithm.Crc32 => new Crc32Accumulator(),
            CodecFileChecksumAlgorithm.XxHash32 => new XxHash32Accumulator(),
            CodecFileChecksumAlgorithm.XxHash64 => new XxHash64Accumulator(),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };
}

/// <summary>Append-only body output that updates the frame checksum as bytes are written.</summary>
public sealed class CodecBodyOutput : IBufferWriter<byte>, IDisposable, ISequentialIndexOutput
{
    private readonly IndexOutput _output;
    private readonly IFileChecksumAccumulator _checksum;
    private readonly Func<bool> _isClosed;
    private byte[]? _buffer = ArrayPool<byte>.Shared.Rent(4096);

    internal CodecBodyOutput(IndexOutput output, IFileChecksumAccumulator checksum, Func<bool> isClosed)
    {
        _output = output;
        _checksum = checksum;
        _isClosed = isClosed;
    }

    public long Position => _output.Position;

    /// <summary>Creates a sequential stream adapter over this body output.</summary>
    public Stream AsStream(bool leaveOpen = true) => new CodecBodyOutputStream(this, leaveOpen);

    public void WriteByte(byte value)
    {
        Span<byte> bytes = stackalloc byte[1] { value };
        WriteBytes(bytes);
    }

    public void WriteInt32(int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        WriteBytes(bytes);
    }

    public void WriteInt64(long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        WriteBytes(bytes);
    }

    public void WriteSingle(float value) => WriteInt32(BitConverter.SingleToInt32Bits(value));

    public void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public void WriteVarInt(int value)
    {
        uint remaining = unchecked((uint)value);
        while (remaining >= 0x80)
        {
            WriteByte((byte)(remaining | 0x80));
            remaining >>= 7;
        }
        WriteByte((byte)remaining);
    }

    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        ThrowIfClosed();
        _checksum.Append(data);
        _output.WriteBytes(data);
    }

    public void Advance(int count)
    {
        ThrowIfClosed();
        if (count < 0 || _buffer is null || count > _buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        WriteBytes(_buffer.AsSpan(0, count));
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer!;
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer;
    }

    internal ulong GetChecksum() => _checksum.GetChecksum();

    public void Dispose()
    {
        if (_buffer is null)
            return;
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = null;
    }

    private void EnsureCapacity(int sizeHint)
    {
        ThrowIfClosed();
        int required = Math.Max(sizeHint, 256);
        if (_buffer!.Length >= required)
            return;
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = ArrayPool<byte>.Shared.Rent(required);
    }

    private void ThrowIfClosed()
    {
        ObjectDisposedException.ThrowIf(_buffer is null, this);
        if (_isClosed())
            throw new InvalidOperationException("The codec write session is no longer writable.");
    }
}

internal sealed class CodecBodyOutputStream : Stream
{
    private readonly CodecBodyOutput _output;
    private readonly bool _leaveOpen;
    private long _position;
    private bool _disposed;

    internal CodecBodyOutputStream(CodecBodyOutput output, bool leaveOpen)
    {
        _output = output;
        _leaveOpen = leaveOpen;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_disposed;
    public override long Length => _position;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if ((uint)offset > (uint)buffer.Length || count < 0 || count > buffer.Length - offset)
            throw new ArgumentOutOfRangeException();
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _output.WriteBytes(buffer);
        _position += buffer.Length;
    }

    public override void WriteByte(byte value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _output.WriteByte(value);
        _position++;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            if (!_leaveOpen)
                _output.Dispose();
        }
        base.Dispose(disposing);
    }
}

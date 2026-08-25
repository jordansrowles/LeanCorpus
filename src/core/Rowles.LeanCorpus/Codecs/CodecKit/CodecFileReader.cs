using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>Structurally opens and validates positively identified canonical codec files.</summary>
public static class CodecFileReader
{
    /// <summary>Opens either a canonical frame or an explicitly supported legacy frame.</summary>
    public static CodecBodyReadSession OpenSupported(
        IndexInput input,
        CodecFileDescriptor descriptor,
        CodecOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(descriptor);
        long start = input.Position;
        if (input.Length - start >= sizeof(int))
        {
            int magic = input.ReadInt32();
            input.Seek(start);
            if (unchecked((uint)magic) == CodecFileWriter.Magic)
            {
                var current = Open(input, descriptor, options);
                current.PositionAtBodyStart();
                return new CodecBodyReadSession(
                    input,
                    current.Metadata.FormatVersion,
                    isCanonical: true,
                    current.Metadata.BodyStart,
                    current.Metadata.BodyLength,
                    current,
                    current.ReadBody,
                    current.ValidateChecksum);
            }
        }

        var legacy = LegacyCodecFileReader.Open(input, descriptor, options);
        legacy.PositionAtBodyStart();
        return new CodecBodyReadSession(
            input,
            legacy.Metadata.FormatVersion,
            isCanonical: false,
            legacy.Metadata.BodyStart,
            legacy.Metadata.BodyLength,
            legacy,
            legacy.ReadBody,
            static () => { });
    }

    /// <summary>Opens a canonical frame and validates it against one expected descriptor.</summary>
    public static CodecReadSession Open(
        IndexInput input,
        CodecFileDescriptor descriptor,
        CodecOptions? options = null,
        bool ownsInput = false)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        CodecFileChecksumAlgorithm requiredChecksum = descriptor.ChecksumPolicy switch
        {
            CodecChecksumPolicy.None => CodecFileChecksumAlgorithm.None,
            CodecChecksumPolicy.XxHash64 => CodecFileChecksumAlgorithm.XxHash64,
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor), descriptor.ChecksumPolicy, "Unknown catalogue checksum policy."),
        };
        var readableVersions = descriptor.SupportedVersions
            .Where(static version => version.IsReadable)
            .Select(static version => version.Version)
            .ToHashSet();
        var session = Open(input, options, descriptor.FormatId, readableVersions, ownsInput);
        if (session.Metadata.ChecksumAlgorithm != requiredChecksum)
        {
            session.Dispose();
            throw new CodecFileException(
                CodecFileErrorCode.UnsupportedChecksumAlgorithm,
                $"Codec format '{descriptor.FormatId}' requires checksum algorithm '{requiredChecksum}', but the file declares '{session.Metadata.ChecksumAlgorithm}'.",
                fileName: input.FilePath is null ? null : Path.GetFileName(input.FilePath),
                formatId: session.Metadata.FormatId,
                frameVersion: session.Metadata.FrameVersion,
                formatVersion: session.Metadata.FormatVersion,
                byteOffset: session.Metadata.BodyStart);
        }

        return session;
    }

    /// <summary>Opens a canonical frame and validates its declared format against a catalogue.</summary>
    public static CodecReadSession Open(
        IndexInput input,
        CodecCatalog catalog,
        CodecOptions? options = null,
        bool ownsInput = false)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var session = Open(input, options, ownsInput: ownsInput);
        if (!catalog.TryGetFile(session.Metadata.FormatId, out var descriptor) || descriptor is null)
        {
            session.Dispose();
            throw new CodecFileException(CodecFileErrorCode.UnknownFormat,
                $"Codec format '{session.Metadata.FormatId}' is not registered in the catalogue.",
                formatId: session.Metadata.FormatId,
                frameVersion: session.Metadata.FrameVersion,
                formatVersion: session.Metadata.FormatVersion,
                byteOffset: session.Metadata.BodyStart);
        }

        bool supported = false;
        foreach (var version in descriptor.SupportedVersions)
        {
            if (version.Version == session.Metadata.FormatVersion && version.IsReadable)
            {
                supported = true;
                break;
            }
        }

        if (!supported)
        {
            session.Dispose();
            throw new CodecFileException(CodecFileErrorCode.UnsupportedFormatVersion,
                $"Codec format '{session.Metadata.FormatId}' version {session.Metadata.FormatVersion} is not readable.",
                formatId: session.Metadata.FormatId,
                frameVersion: session.Metadata.FrameVersion,
                formatVersion: session.Metadata.FormatVersion,
                byteOffset: session.Metadata.BodyStart);
        }

        return session;
    }

    /// <summary>Opens a canonical frame without scanning its body checksum.</summary>
    public static CodecReadSession Open(
        IndexInput input,
        CodecOptions? options = null,
        string? expectedFormatId = null,
        IReadOnlySet<int>? supportedFormatVersions = null,
        bool ownsInput = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        options ??= CodecOptions.Default;
        long frameStart = input.Position;
        string? fileName = input.FilePath is null ? null : Path.GetFileName(input.FilePath);
        long frameLength = input.Length - frameStart;
        if (frameLength > options.MaxCodecFileBytes)
            throw Error(CodecFileErrorCode.LimitExceeded, fileName, null, null, null, frameStart,
                $"Codec file length {frameLength} exceeds the configured maximum {options.MaxCodecFileBytes} bytes.");
        if (frameLength < CodecFileWriter.FixedHeaderLength + 1 + CodecFileWriter.FooterLength)
            throw Error(CodecFileErrorCode.TruncatedHeader, fileName, null, null, null, frameStart,
                "Codec file is too short to contain a canonical frame.");

        int magic;
        try
        {
            magic = input.ReadInt32();
        }
        catch (EndOfStreamException ex)
        {
            throw Error(CodecFileErrorCode.TruncatedHeader, fileName, null, null, null, frameStart,
                "Codec file has a truncated frame magic.", ex);
        }

        if (unchecked((uint)magic) != CodecFileWriter.Magic)
            throw Error(CodecFileErrorCode.InvalidMagic, fileName, null, null, null, frameStart, "Codec file has an invalid canonical frame magic.");

        byte frameVersion;
        byte identifierLength;
        int formatVersion;
        CodecFrameFlags flags;
        CodecFileChecksumAlgorithm checksumAlgorithm;
        byte reserved;
        try
        {
            frameVersion = input.ReadByte();
            identifierLength = input.ReadByte();
            formatVersion = input.ReadInt32();
            flags = unchecked((CodecFrameFlags)(uint)input.ReadInt32());
            checksumAlgorithm = (CodecFileChecksumAlgorithm)input.ReadByte();
            reserved = input.ReadByte();
        }
        catch (EndOfStreamException ex)
        {
            throw Error(CodecFileErrorCode.TruncatedHeader, fileName, null, null, null, input.Position,
                "Codec file has a truncated canonical frame header.", ex);
        }

        if (frameVersion != CodecFileWriter.CurrentFrameVersion)
            throw Error(CodecFileErrorCode.UnsupportedFrameVersion, fileName, null, frameVersion, formatVersion, frameStart + 4,
                $"Unsupported codec frame version {frameVersion}.");
        if (identifierLength is 0 or > CodecFileWriter.MaximumFormatIdBytes)
            throw Error(CodecFileErrorCode.InvalidFormatIdentifier, fileName, null, frameVersion, formatVersion, frameStart + 5,
                $"Codec frame format identifier length {identifierLength} is invalid.");
        if (formatVersion <= 0)
            throw Error(CodecFileErrorCode.UnsupportedFormatVersion, fileName, null, frameVersion, formatVersion, frameStart + 6,
                $"Codec format version {formatVersion} is invalid.");
        if (flags != CodecFrameFlags.None)
            throw Error(CodecFileErrorCode.InvalidFlags, fileName, null, frameVersion, formatVersion, frameStart + 10,
                $"Codec frame contains unsupported flags 0x{(uint)flags:x8}.");
        if (checksumAlgorithm is < CodecFileChecksumAlgorithm.None or > CodecFileChecksumAlgorithm.XxHash64)
            throw Error(CodecFileErrorCode.UnsupportedChecksumAlgorithm, fileName, null, frameVersion, formatVersion, frameStart + 14,
                $"Codec frame checksum algorithm {(byte)checksumAlgorithm} is unsupported.");
        if (reserved != 0)
            throw Error(CodecFileErrorCode.InvalidFlags, fileName, null, frameVersion, formatVersion, frameStart + 15,
                "Codec frame reserved byte must be zero.");

        string formatId;
        try
        {
            byte[] identifier = input.ReadBytes(identifierLength);
            for (int i = 0; i < identifier.Length; i++)
            {
                byte value = identifier[i];
                bool valid = value is >= (byte)'a' and <= (byte)'z'
                    || value is >= (byte)'0' and <= (byte)'9'
                    || value is (byte)'.' or (byte)'-';
                if (!valid)
                    throw Error(CodecFileErrorCode.InvalidFormatIdentifier, fileName, null, frameVersion, formatVersion, frameStart + CodecFileWriter.FixedHeaderLength + i,
                        "Codec frame format identifier is not stable lowercase ASCII.");
            }
            formatId = Encoding.ASCII.GetString(identifier);
            CodecFileWriter.EncodeAndValidateFormatId(formatId);
        }
        catch (EndOfStreamException ex)
        {
            throw Error(CodecFileErrorCode.TruncatedHeader, fileName, null, frameVersion, formatVersion, input.Position,
                "Codec file has a truncated format identifier.", ex);
        }
        catch (ArgumentException ex)
        {
            throw Error(CodecFileErrorCode.InvalidFormatIdentifier, fileName, null, frameVersion, formatVersion, input.Position,
                "Codec frame format identifier is invalid.", ex);
        }

        if (expectedFormatId is not null && !formatId.Equals(expectedFormatId, StringComparison.Ordinal))
            throw Error(CodecFileErrorCode.FormatMismatch, fileName, formatId, frameVersion, formatVersion, frameStart,
                $"Codec file declares format '{formatId}' but '{expectedFormatId}' was expected.");
        if (supportedFormatVersions is not null && !supportedFormatVersions.Contains(formatVersion))
            throw Error(CodecFileErrorCode.UnsupportedFormatVersion, fileName, formatId, frameVersion, formatVersion, frameStart + 6,
                $"Codec format '{formatId}' version {formatVersion} is not supported.");

        long bodyStart = input.Position;
        long footerStart = input.Length - CodecFileWriter.FooterLength;
        if (footerStart < bodyStart)
            throw Error(CodecFileErrorCode.TruncatedBody, fileName, formatId, frameVersion, formatVersion, bodyStart,
                "Codec file is truncated before its footer.");

        input.Seek(footerStart);
        long bodyLength;
        ulong storedChecksum;
        try
        {
            bodyLength = input.ReadInt64();
            storedChecksum = unchecked((ulong)input.ReadInt64());
        }
        catch (EndOfStreamException ex)
        {
            throw Error(CodecFileErrorCode.TruncatedBody, fileName, formatId, frameVersion, formatVersion, footerStart,
                "Codec file has a truncated footer.", ex);
        }

        if (bodyLength < 0 || bodyLength != footerStart - bodyStart)
            throw Error(CodecFileErrorCode.InvalidBodyLength, fileName, formatId, frameVersion, formatVersion, footerStart,
                $"Codec file declares body length {bodyLength}, but the physical body length is {footerStart - bodyStart}.");
        if (checksumAlgorithm is CodecFileChecksumAlgorithm.None && storedChecksum != 0
            || checksumAlgorithm is CodecFileChecksumAlgorithm.Crc32 or CodecFileChecksumAlgorithm.XxHash32 && storedChecksum > uint.MaxValue)
            throw Error(CodecFileErrorCode.ChecksumMismatch, fileName, formatId, frameVersion, formatVersion, footerStart + sizeof(long),
                "Codec file checksum footer contains non-zero reserved bits.");

        input.Seek(bodyStart);
        var metadata = new CodecFrameMetadata(formatId, frameVersion, formatVersion, flags, checksumAlgorithm, bodyStart, bodyLength, storedChecksum);
        return new CodecReadSession(input, metadata, options, ownsInput);
    }

    private static CodecFileException Error(
        CodecFileErrorCode code,
        string? fileName,
        string? formatId,
        byte? frameVersion,
        int? formatVersion,
        long? offset,
        string message,
        Exception? inner = null)
        => new(code, message, fileName, formatId, frameVersion, formatVersion, offset, inner);
}

/// <summary>Provides one body-positioned input across canonical and supported legacy frames.</summary>
public sealed class CodecBodyReadSession : IDisposable
{
    private readonly IDisposable _frameSession;
    private readonly Func<byte[]> _readBody;
    private readonly Action _validateChecksum;

    internal CodecBodyReadSession(
        IndexInput input,
        int formatVersion,
        bool isCanonical,
        long bodyStart,
        long bodyLength,
        IDisposable frameSession,
        Func<byte[]> readBody,
        Action validateChecksum)
    {
        Input = input;
        FormatVersion = formatVersion;
        IsCanonical = isCanonical;
        BodyStart = bodyStart;
        BodyLength = bodyLength;
        _frameSession = frameSession;
        _readBody = readBody;
        _validateChecksum = validateChecksum;
    }

    internal IndexInput Input { get; }

    public int FormatVersion { get; }

    public bool IsCanonical { get; }

    public long BodyStart { get; }

    public long BodyLength { get; }

    /// <summary>Materialises the body using the configured operation-specific limit.</summary>
    public byte[] ReadBody() => _readBody();

    /// <summary>Validates the canonical body checksum, or does nothing for a legacy frame.</summary>
    public void ValidateChecksum() => _validateChecksum();

    /// <summary>Opens a seekable stream bounded to the codec body.</summary>
    public Stream OpenBodyStream()
        => new IndexInputStream(Input, BodyStart, BodyLength, leaveOpen: true);

    /// <summary>Opens a separately owned random-access input bounded to the codec body.</summary>
    public IndexInput OpenBodyInput() => Input.OpenSlice(BodyStart, BodyLength);

    public void Dispose() => _frameSession.Dispose();
}

/// <summary>An opened canonical frame with explicit materialising and checksum operations.</summary>
public sealed class CodecReadSession : IDisposable
{
    private readonly IndexInput _input;
    private readonly CodecOptions _options;
    private readonly bool _ownsInput;
    private bool _disposed;

    internal CodecReadSession(IndexInput input, CodecFrameMetadata metadata, CodecOptions options, bool ownsInput)
    {
        _input = input;
        Metadata = metadata;
        _options = options;
        _ownsInput = ownsInput;
    }

    public CodecFrameMetadata Metadata { get; }

    public long BodyEnd => checked(Metadata.BodyStart + Metadata.BodyLength);

    /// <summary>Opens a separately owned random-access input bounded to the codec body.</summary>
    public IndexInput OpenBodyInput()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _input.OpenSlice(Metadata.BodyStart, Metadata.BodyLength);
    }

    /// <summary>Returns the logical input positioned at the body start.</summary>
    internal IndexInput PositionAtBodyStart()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.Seek(Metadata.BodyStart);
        return _input;
    }

    /// <summary>Materialises the body within the configured operation-specific limit and verifies its checksum.</summary>
    public byte[] ReadBody()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Metadata.BodyLength > _options.MaxMaterialisedBodyBytes || Metadata.BodyLength > int.MaxValue)
            throw new CodecFileException(CodecFileErrorCode.LimitExceeded,
                $"Codec body length {Metadata.BodyLength} exceeds the materialisation limit {_options.MaxMaterialisedBodyBytes} bytes.",
                formatId: Metadata.FormatId, frameVersion: Metadata.FrameVersion, formatVersion: Metadata.FormatVersion, byteOffset: Metadata.BodyStart);

        _input.Seek(Metadata.BodyStart);
        byte[] body = _input.ReadBytes(checked((int)Metadata.BodyLength));
        ValidateChecksum(body);
        return body;
    }

    /// <summary>Streams over the body and verifies the stored checksum without materialising it.</summary>
    public void ValidateChecksum()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var accumulator = CodecWriteSession.CreateAccumulator(Metadata.ChecksumAlgorithm);
        long originalPosition = _input.Position;
        try
        {
            _input.Seek(Metadata.BodyStart);
            long remaining = Metadata.BodyLength;
            while (remaining > 0)
            {
                int count = (int)Math.Min(64 * 1024, remaining);
                accumulator.Append(_input.BorrowSpan(count));
                remaining -= count;
            }
            ValidateChecksum(accumulator.GetChecksum());
        }
        finally
        {
            _input.Seek(originalPosition);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsInput)
            _input.Dispose();
    }

    private void ValidateChecksum(ReadOnlySpan<byte> body)
    {
        var accumulator = CodecWriteSession.CreateAccumulator(Metadata.ChecksumAlgorithm);
        accumulator.Append(body);
        ValidateChecksum(accumulator.GetChecksum());
    }

    private void ValidateChecksum(ulong computed)
    {
        if (computed != Metadata.StoredChecksum)
            throw new CodecFileException(CodecFileErrorCode.ChecksumMismatch,
                $"Codec file checksum mismatch for format '{Metadata.FormatId}'.",
                formatId: Metadata.FormatId, frameVersion: Metadata.FrameVersion, formatVersion: Metadata.FormatVersion, byteOffset: BodyEnd + sizeof(long));
    }
}

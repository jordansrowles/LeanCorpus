using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>Legacy file framing kinds retained for reading supported 2.x indexes.</summary>
public enum LegacyCodecFrameKind
{
    Envelope,
    Trailer,
}

/// <summary>Metadata parsed from a supported legacy codec-file frame.</summary>
public sealed record LegacyCodecFrameMetadata(
    string FormatId,
    LegacyCodecFrameKind FrameKind,
    int FormatVersion,
    long BodyStart,
    long BodyLength);

/// <summary>Opens the heuristic envelope and trailer frames used before Frame v1.</summary>
public static class LegacyCodecFileReader
{
    /// <summary>Opens a legacy frame using a catalogue descriptor for format and version validation.</summary>
    public static LegacyCodecReadSession Open(
        IndexInput input,
        CodecFileDescriptor descriptor,
        CodecOptions? options = null,
        bool ownsInput = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(descriptor);
        options ??= CodecOptions.Default;

        long start = input.Position;
        long frameLength = input.Length - start;
        if (frameLength < 2)
            throw new CodecFileException(CodecFileErrorCode.TruncatedHeader, "Legacy codec file is truncated.", formatId: descriptor.FormatId, byteOffset: start);
        if (frameLength > options.MaxCodecFileBytes)
            throw new CodecFileException(CodecFileErrorCode.LimitExceeded,
                $"Legacy codec file length {frameLength} exceeds the configured maximum {options.MaxCodecFileBytes} bytes.",
                formatId: descriptor.FormatId, byteOffset: start);

        int version = input.ReadByte();
        CodecVersionDescriptor? versionDescriptor = GetReadableVersion(descriptor, version);
        if (versionDescriptor is null)
            throw new CodecFileException(CodecFileErrorCode.UnsupportedFormatVersion,
                $"Legacy codec format '{descriptor.FormatId}' version {version} is not readable.",
                formatId: descriptor.FormatId, formatVersion: version, byteOffset: start);

        if (frameLength >= 9)
        {
            input.Seek(input.Length - sizeof(long));
            long trailerBodyLength = input.ReadInt64();
            if (trailerBodyLength >= 0 && 1L + trailerBodyLength + sizeof(long) == frameLength)
            {
                if ((versionDescriptor.LegacyFraming & CodecLegacyFraming.CodecKitTrailer) == 0)
                    throw UnsupportedFraming(descriptor, version, LegacyCodecFrameKind.Trailer, start);
                long bodyStart = start + 1;
                input.Seek(bodyStart);
                return new LegacyCodecReadSession(
                    input,
                    new LegacyCodecFrameMetadata(descriptor.FormatId, LegacyCodecFrameKind.Trailer, version, bodyStart, trailerBodyLength),
                    options,
                    ownsInput);
            }
        }

        input.Seek(start + 1);
        if ((versionDescriptor.LegacyFraming & CodecLegacyFraming.CodecKitEnvelope) == 0)
            throw UnsupportedFraming(descriptor, version, LegacyCodecFrameKind.Envelope, start);
        long envelopeBodyLength = ReadVarInt64(input);
        long envelopeBodyStart = input.Position;
        if (envelopeBodyLength < 0 || envelopeBodyLength != input.Length - envelopeBodyStart)
            throw new CodecFileException(CodecFileErrorCode.InvalidBodyLength,
                $"Legacy codec envelope declares body length {envelopeBodyLength}, but the physical body length is {input.Length - envelopeBodyStart}.",
                formatId: descriptor.FormatId, formatVersion: version, byteOffset: envelopeBodyStart);

        return new LegacyCodecReadSession(
            input,
            new LegacyCodecFrameMetadata(descriptor.FormatId, LegacyCodecFrameKind.Envelope, version, envelopeBodyStart, envelopeBodyLength),
            options,
            ownsInput);
    }

    private static CodecVersionDescriptor? GetReadableVersion(CodecFileDescriptor descriptor, int version)
    {
        foreach (var candidate in descriptor.SupportedVersions)
        {
            if (candidate.Version == version)
                return candidate.IsReadable ? candidate : null;
        }
        return null;
    }

    private static CodecFileException UnsupportedFraming(
        CodecFileDescriptor descriptor,
        int version,
        LegacyCodecFrameKind frameKind,
        long offset)
        => new(
            CodecFileErrorCode.UnsupportedFormatVersion,
            $"Codec format '{descriptor.FormatId}' version {version} does not support the detected legacy {frameKind} frame.",
            formatId: descriptor.FormatId,
            formatVersion: version,
            byteOffset: offset);

    private static long ReadVarInt64(IndexInput input)
    {
        ulong result = 0;
        for (int shift = 0; shift < 70; shift += 7)
        {
            byte value;
            try
            {
                value = input.ReadByte();
            }
            catch (EndOfStreamException ex)
            {
                throw new CodecFileException(CodecFileErrorCode.TruncatedHeader, "Legacy codec envelope length is truncated.", byteOffset: input.Position, innerException: ex);
            }

            result |= (ulong)(value & 0x7f) << shift;
            if ((value & 0x80) == 0)
            {
                long signed = (long)(result >> 1);
                return (result & 1) == 0 ? signed : ~signed;
            }
        }
        throw new CodecFileException(CodecFileErrorCode.InvalidBodyLength, "Legacy codec envelope length is malformed.", byteOffset: input.Position);
    }
}

/// <summary>An opened legacy frame with bounded materialisation support.</summary>
public sealed class LegacyCodecReadSession : IDisposable
{
    private readonly IndexInput _input;
    private readonly CodecOptions _options;
    private readonly bool _ownsInput;
    private bool _disposed;

    internal LegacyCodecReadSession(IndexInput input, LegacyCodecFrameMetadata metadata, CodecOptions options, bool ownsInput)
    {
        _input = input;
        Metadata = metadata;
        _options = options;
        _ownsInput = ownsInput;
    }

    public LegacyCodecFrameMetadata Metadata { get; }

    internal IndexInput PositionAtBodyStart()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.Seek(Metadata.BodyStart);
        return _input;
    }

    public byte[] ReadBody()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Metadata.BodyLength > _options.MaxMaterialisedBodyBytes || Metadata.BodyLength > int.MaxValue)
            throw new CodecFileException(CodecFileErrorCode.LimitExceeded,
                $"Legacy codec body length {Metadata.BodyLength} exceeds the materialisation limit {_options.MaxMaterialisedBodyBytes} bytes.",
                formatId: Metadata.FormatId, formatVersion: Metadata.FormatVersion, byteOffset: Metadata.BodyStart);
        _input.Seek(Metadata.BodyStart);
        return _input.ReadBytes(checked((int)Metadata.BodyLength));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsInput)
            _input.Dispose();
    }
}

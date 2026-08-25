namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>Checksum algorithms supported by the canonical codec-file frame.</summary>
public enum CodecFileChecksumAlgorithm : byte
{
    /// <summary>No checksum. This requires a documented descriptor-level opt-out.</summary>
    None = 0,

    /// <summary>CRC-32/ISO-HDLC stored in the low 32 bits of the footer checksum.</summary>
    Crc32 = 1,

    /// <summary>xxHash32 stored in the low 32 bits of the footer checksum.</summary>
    XxHash32 = 2,

    /// <summary>xxHash64.</summary>
    XxHash64 = 3,
}

/// <summary>Flags carried by the canonical codec-file frame.</summary>
[Flags]
public enum CodecFrameFlags : uint
{
    /// <summary>No optional frame behaviour.</summary>
    None = 0,
}

/// <summary>Machine-readable canonical codec-file failure categories.</summary>
public enum CodecFileErrorCode
{
    InvalidMagic,
    UnsupportedFrameVersion,
    UnknownFormat,
    UnsupportedFormatVersion,
    TruncatedHeader,
    TruncatedBody,
    InvalidBodyLength,
    ChecksumMismatch,
    FormatMismatch,
    LimitExceeded,
    InvalidFormatIdentifier,
    UnsupportedChecksumAlgorithm,
    InvalidFlags,
    SemanticValidationFailure,
}

/// <summary>Describes a structurally opened canonical codec file.</summary>
/// <param name="FormatId">Stable catalogue format identifier.</param>
/// <param name="FrameVersion">Physical frame version.</param>
/// <param name="FormatVersion">Semantic body-format version.</param>
/// <param name="Flags">Frame flags.</param>
/// <param name="ChecksumAlgorithm">Body checksum algorithm.</param>
/// <param name="BodyStart">Logical input offset at which the body begins.</param>
/// <param name="BodyLength">Body length in bytes.</param>
/// <param name="StoredChecksum">Checksum stored in the fixed footer.</param>
public sealed record CodecFrameMetadata(
    string FormatId,
    byte FrameVersion,
    int FormatVersion,
    CodecFrameFlags Flags,
    CodecFileChecksumAlgorithm ChecksumAlgorithm,
    long BodyStart,
    long BodyLength,
    ulong StoredChecksum);

/// <summary>Represents a structured failure while opening or validating a canonical codec file.</summary>
public sealed class CodecFileException : IOException
{
    public CodecFileException(
        CodecFileErrorCode errorCode,
        string message,
        string? fileName = null,
        string? formatId = null,
        byte? frameVersion = null,
        int? formatVersion = null,
        long? byteOffset = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        FileName = fileName;
        FormatId = formatId;
        FrameVersion = frameVersion;
        FormatVersion = formatVersion;
        ByteOffset = byteOffset;
    }

    public CodecFileErrorCode ErrorCode { get; }

    public string? FileName { get; }

    public string? FormatId { get; }

    public byte? FrameVersion { get; }

    public int? FormatVersion { get; }

    public long? ByteOffset { get; }
}

using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Index.Format;

/// <summary>Identifies the framing detected for a logical index file.</summary>
public enum CodecFileFrameKind
{
    /// <summary>The framing could not be identified.</summary>
    Unknown = 0,

    /// <summary>The file uses the canonical self-identifying CodecKit frame.</summary>
    Canonical = 1,

    /// <summary>The file uses the legacy CodecKit length envelope.</summary>
    LegacyEnvelope = 2,

    /// <summary>The file uses the legacy CodecKit streaming trailer.</summary>
    LegacyTrailer = 3,

    /// <summary>The file uses a declared format-specific legacy header.</summary>
    LegacyCustomHeader = 4,

    /// <summary>The file uses a declared legacy headerless representation.</summary>
    LegacyHeaderless = 5,

    /// <summary>The format is owned by an external serialiser.</summary>
    External = 6,

    /// <summary>The file is a storage container whose members are inspected separately.</summary>
    Container = 7,
}

/// <summary>Describes whether a magic value applies and, when it does, whether it is valid.</summary>
public enum CodecMagicStatus
{
    /// <summary>The format does not define a magic value.</summary>
    NotApplicable = 0,

    /// <summary>The expected magic value was present.</summary>
    Valid = 1,

    /// <summary>A magic value applies but was invalid.</summary>
    Invalid = 2,

    /// <summary>The file could not be read far enough to determine the magic value.</summary>
    Unknown = 3,
}

/// <summary>Describes checksum availability and verification state.</summary>
public enum CodecChecksumStatus
{
    /// <summary>The detected framing does not provide a checksum.</summary>
    NotApplicable = 0,

    /// <summary>A checksum is present but was not recomputed.</summary>
    NotVerified = 1,

    /// <summary>The recomputed checksum matches the stored checksum.</summary>
    Valid = 2,

    /// <summary>The recomputed checksum does not match the stored checksum.</summary>
    Invalid = 3,

    /// <summary>Checksum state could not be determined.</summary>
    Unknown = 4,
}

/// <summary>Identifies where a logical file is physically stored.</summary>
public enum CodecPhysicalLocationKind
{
    /// <summary>The logical file is a loose file in the index directory.</summary>
    LooseFile = 0,

    /// <summary>The logical file is a bounded member of a compound segment.</summary>
    CompoundMember = 1,

    /// <summary>The logical file is itself a compound container.</summary>
    CompoundContainer = 2,
}

/// <summary>
/// Describes one index file and the codec version detected from its header.
/// </summary>
public sealed record CodecFileInventory
{
    private int? _formatVersion;
    private int? _currentFormatVersion;
    private CodecMagicStatus _magicStatus = CodecMagicStatus.Unknown;

    /// <summary>Gets the file name relative to the index directory.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the file extension.</summary>
    public required string Extension { get; init; }

    /// <summary>Gets the logical codec name.</summary>
    public required string CodecName { get; init; }

    /// <summary>Gets the stable catalogue format identifier, when recognised.</summary>
    public string? FormatId { get; init; }

    /// <summary>Gets the stable catalogue family identifier, when recognised.</summary>
    public string? FamilyId { get; init; }

    /// <summary>Gets the human-readable catalogue family name, when recognised.</summary>
    public string? FamilyName { get; init; }

    /// <summary>Gets the detected frame kind.</summary>
    public CodecFileFrameKind FrameKind { get; init; }

    /// <summary>Gets the detected frame version, or <c>null</c> for legacy and external framing.</summary>
    public int? FrameVersion { get; init; }

    /// <summary>Gets the detected body-format version, or <c>null</c> for versionless formats.</summary>
    public int? FormatVersion
    {
        get => _formatVersion;
        init => _formatVersion = value;
    }

    /// <summary>Gets the current body-format version from the catalogue.</summary>
    public int? CurrentFormatVersion
    {
        get => _currentFormatVersion;
        init => _currentFormatVersion = value;
    }

    /// <summary>Gets the detected on-disk codec version, or <c>null</c> when the file has no standard codec header.</summary>
    public byte? Version
    {
        get => FormatVersion is >= byte.MinValue and <= byte.MaxValue
            ? (byte)FormatVersion.Value
            : null;
        init => _formatVersion = value;
    }

    /// <summary>Gets the current codec version supported by this build, or <c>null</c> when the file has no standard codec header.</summary>
    public byte? CurrentVersion
    {
        get => CurrentFormatVersion is >= byte.MinValue and <= byte.MaxValue
            ? (byte)CurrentFormatVersion.Value
            : null;
        init => _currentFormatVersion = value;
    }

    /// <summary>Gets the explicit magic applicability and validity state.</summary>
    public CodecMagicStatus MagicStatus
    {
        get => _magicStatus;
        init => _magicStatus = value;
    }

    /// <summary>Gets a value indicating whether the file has the expected LeanCorpus codec magic.</summary>
    public bool HasValidMagic
    {
        get => MagicStatus is CodecMagicStatus.Valid or CodecMagicStatus.NotApplicable;
        init => _magicStatus = value ? CodecMagicStatus.Valid : CodecMagicStatus.Invalid;
    }

    /// <summary>Gets the canonical checksum algorithm, when the frame records one.</summary>
    public CodecFileChecksumAlgorithm? ChecksumAlgorithm { get; init; }

    /// <summary>Gets whether an available checksum was verified successfully.</summary>
    public CodecChecksumStatus ChecksumStatus { get; init; } = CodecChecksumStatus.Unknown;

    /// <summary>Gets a value indicating whether this build can read the detected version.</summary>
    public required bool IsSupported { get; init; }

    /// <summary>Gets a value indicating whether the file is already at the current codec version.</summary>
    public required bool IsCurrent { get; init; }

    /// <summary>Gets the file length in bytes when requested.</summary>
    public long? Length { get; init; }

    /// <summary>Gets the related segment ID, when known.</summary>
    public string? SegmentId { get; init; }

    /// <summary>Gets the related field name, when known.</summary>
    public string? FieldName { get; init; }

    /// <summary>Gets where the logical file is physically stored.</summary>
    public CodecPhysicalLocationKind PhysicalLocation { get; init; }

    /// <summary>Gets the physical file name, which is the compound container for a compound member.</summary>
    public string PhysicalFileName { get; init; } = string.Empty;

    /// <summary>Gets the owning compound file name for a compound member.</summary>
    public string? CompoundFileName { get; init; }

    /// <summary>Gets whether this inventory entry describes a compound member.</summary>
    public bool IsCompoundMember => PhysicalLocation == CodecPhysicalLocationKind.CompoundMember;

    /// <summary>Gets whether the logical file resolved to a registered catalogue format.</summary>
    public bool IsKnownFormat { get; init; } = true;

    /// <summary>Gets the structured frame error category, when inspection failed.</summary>
    public CodecFileErrorCode? ErrorCode { get; init; }
}

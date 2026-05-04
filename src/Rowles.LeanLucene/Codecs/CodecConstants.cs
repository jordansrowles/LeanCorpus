namespace Rowles.LeanLucene.Codecs;

/// <summary>
/// Shared magic number and format version constants for all codec file types.
/// Every codec file starts with [int32: Magic][byte: Version].
/// </summary>
internal static class CodecConstants
{
    /// <summary>Magic number written as the first 4 bytes of every LeanLucene codec file.</summary>
    public const int Magic = 0x4C4C_4E31; // "LLN1" in ASCII

    // Per-format version numbers — increment when the binary layout changes.
    public const byte TermDictionaryVersion = 2;
    public const byte PostingsVersion = 3;
    public const byte NormsVersion = 1;
    public const byte VectorVersion = 1;
    public const byte HnswVersion = 1;
    public const byte StoredFieldsVersion = 5; // v5: LZ4 block compression (was v4 Brotli)
    public const byte TermVectorsVersion = 1;
    public const byte NumericDocValuesVersion = 2;
    public const byte SortedDocValuesVersion = 2;
    public const byte BKDVersion = 1;
    public const byte FieldLengthVersion = 2; // v2: VarInt field lengths (was fixed ushort)
    public const byte RoaringBitmapVersion = 1;

    /// <summary>Header size in bytes: 4 (magic) + 1 (version).</summary>
    public const int HeaderSize = 5;

    /// <summary>Writes the standard codec header to an IndexOutput.</summary>
    public static void WriteHeader(Store.IndexOutput output, byte version)
    {
        output.WriteInt32(Magic);
        output.WriteByte(version);
    }

    /// <summary>Writes the standard codec header to a BinaryWriter.</summary>
    public static void WriteHeader(BinaryWriter writer, byte version)
    {
        writer.Write(Magic);
        writer.Write(version);
    }

    /// <summary>Validates the magic number and returns the version byte from an IndexInput.</summary>
    public static byte ReadHeaderVersion(Store.IndexInput input, byte maxSupportedVersion, string fileType)
    {
        int magic = input.ReadInt32();
        if (magic != Magic)
            throw new InvalidDataException(
                $"Invalid {fileType} file: expected magic 0x{Magic:X8}, got 0x{magic:X8}. " +
                "The file may be corrupted or from an incompatible version.");
        byte version = input.ReadByte();
        if (version > maxSupportedVersion)
            throw new InvalidDataException(
                $"Unsupported {fileType} format version {version}. " +
                $"This build supports up to version {maxSupportedVersion}. " +
                "Please upgrade LeanLucene.");
        return version;
    }

    /// <summary>Validates the magic number and version from an IndexInput. Throws on mismatch.</summary>
    public static void ValidateHeader(Store.IndexInput input, byte expectedVersion, string fileType)
    {
        int magic = input.ReadInt32();
        if (magic != Magic)
            throw new InvalidDataException(
                $"Invalid {fileType} file: expected magic 0x{Magic:X8}, got 0x{magic:X8}. " +
                "The file may be corrupted or from an incompatible version.");
        byte version = input.ReadByte();
        if (version > expectedVersion)
            throw new InvalidDataException(
                $"Unsupported {fileType} format version {version}. " +
                $"This build supports up to version {expectedVersion}. " +
                "Please upgrade LeanLucene.");
    }

    /// <summary>Validates the magic number and version from a BinaryReader. Throws on mismatch.</summary>
    public static void ValidateHeader(BinaryReader reader, byte expectedVersion, string fileType)
    {
        int magic = reader.ReadInt32();
        if (magic != Magic)
            throw new InvalidDataException(
                $"Invalid {fileType} file: expected magic 0x{Magic:X8}, got 0x{magic:X8}. " +
                "The file may be corrupted or from an incompatible version.");
        byte version = reader.ReadByte();
        if (version > expectedVersion)
            throw new InvalidDataException(
                $"Unsupported {fileType} format version {version}. " +
                $"This build supports up to version {expectedVersion}. " +
                "Please upgrade LeanLucene.");
    }
}

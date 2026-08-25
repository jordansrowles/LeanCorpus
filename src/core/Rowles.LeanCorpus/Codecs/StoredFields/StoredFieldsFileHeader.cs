using System.IO;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.StoredFields;

/// <summary>
/// Legacy header reader for stored-fields files.
/// v1 used the CodecKit envelope: [version:byte][VarInt64 bodyLen][body].
/// v2 streams directly: [version:byte][body] (ADR008 custom header).
/// v3 legacy data uses the CodecKit trailer while legacy index files retain the custom header.
/// Current v3 writes use the canonical CodecKit frame.
/// </summary>
internal static class StoredFieldsFileHeader
{
    internal const byte V1 = 1;
    internal const byte V2 = 2;
    internal const byte V3 = 3;

    /// <summary>Size of the v2 .fdt header: version + blockSize + compression.</summary>
    internal const int V2FdtHeaderSize = sizeof(byte) + sizeof(int) + sizeof(byte);

    /// <summary>Size of the v2 .fdx header: version + blockSize + docCount + blockCount.</summary>
    internal const int V2FdxHeaderSize = sizeof(byte) + sizeof(int) + sizeof(int) + sizeof(int);

    /// <summary>
    /// Reads the version byte and skips any v1-only length prefix.
    /// Returns the raw version byte; callers must validate against the current version.
    /// </summary>
    internal static byte ReadVersion(BinaryReader reader)
    {
        byte version = reader.ReadByte();

        if (version == V1)
            SkipVarInt64(reader);

        return version;
    }

    /// <summary>
    /// Reads the version from a logical file input and skips any v1-only length prefix.
    /// </summary>
    internal static byte ReadVersion(IndexInput input)
    {
        byte version = input.ReadByte();

        if (version == V1)
            SkipVarInt64(input);

        return version;
    }

    private static void SkipVarInt64(BinaryReader reader)
    {
        for (int i = 0; i < 10; i++)
        {
            byte b = reader.ReadByte();
            if ((b & 0x80) == 0) return;
        }

        throw new InvalidDataException("VarInt64 body length is malformed (exceeds 10 bytes).");
    }

    private static void SkipVarInt64(IndexInput input)
    {
        for (int i = 0; i < 10; i++)
        {
            byte b = input.ReadByte();
            if ((b & 0x80) == 0) return;
        }

        throw new InvalidDataException("VarInt64 body length is malformed (exceeds 10 bytes).");
    }
}

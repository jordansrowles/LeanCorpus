using System.IO;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.StoredFields;

/// <summary>
/// Manual header read/write for stored-fields files.
/// v1 used the CodecKit envelope: [version:byte][VarInt64 bodyLen][body].
/// v2 streams directly: [version:byte][body] (ADR008 custom header).
/// v3 uses the CodecKit trailer: [version:byte][body][bodyLen:int64].
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
    /// Writes the v3 .fdx header: a single version byte followed by block metadata.
    /// Body format is identical to v2; only the version byte changes (v2 to v3).
    /// </summary>
    internal static void WriteV3FdxHeader(IndexOutput output, int blockSize, int docCount, int blockCount)
    {
        output.WriteByte(V3);
        output.WriteInt32(blockSize);
        output.WriteInt32(docCount);
        output.WriteInt32(blockCount);
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
}

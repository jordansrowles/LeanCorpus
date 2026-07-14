using System.IO;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Postings;

/// <summary>
/// Manual header read/write for postings files.
/// v1 used the CodecKit envelope: [version:byte][VarInt64 bodyLen][body].
/// v2 streams directly: [version:byte][body] (ADR008 custom header).
/// v3 uses the CodecKit trailer: [version:byte][body][bodyLen:int64].
/// </summary>
internal static class PostingsFileHeader
{
    internal const byte V1 = 1;
    internal const byte V2 = 2;
    internal const byte V3 = 3;

    /// <summary>
    /// Reads the version byte and skips any v1-only VarInt64 length prefix.
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
    /// <see cref="IndexInput"/> overload of <see cref="ReadVersion(BinaryReader)"/>.
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

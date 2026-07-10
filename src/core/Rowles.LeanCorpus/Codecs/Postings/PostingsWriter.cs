using System.Buffers;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Postings;

/// <summary>
/// Writes delta-encoded postings lists for a given term.
/// Deltas are encoded as variable-length integers (VarInt/LEB128) for compactness.
/// </summary>
internal static class PostingsWriter
{
    internal static void Write(string filePath, string term, int[] docIds)
    {
        var bodyBuf = new ArrayBufferWriter<byte>(4096);

        bodyBuf.WriteInt32(term.Length);
        bodyBuf.WriteChars(term.AsSpan());
        bodyBuf.WriteInt32(docIds.Length);

        int prev = 0;
        for (int i = 0; i < docIds.Length; i++)
        {
            WriteVarInt(bodyBuf, docIds[i] - prev);
            prev = docIds[i];
        }

        using var output = new IndexOutput(filePath, durable: true);
        PostingsFileHeader.WriteV2Header(output);
        output.WriteBytes(bodyBuf.WrittenSpan);
    }

    /// <summary>Writes a non-negative integer using variable-length encoding (LEB128).</summary>
    public static void WriteVarInt(IBufferWriter<byte> writer, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            writer.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }
        writer.WriteByte((byte)v);
    }
}

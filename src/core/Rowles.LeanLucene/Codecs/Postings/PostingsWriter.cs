namespace Rowles.LeanLucene.Codecs.Postings;

/// <summary>
/// Writes delta-encoded postings lists for a given term.
/// Deltas are encoded as variable-length integers (VarInt/LEB128) for compactness.
/// </summary>
internal static class PostingsWriter
{
    internal static void Write(string filePath, string term, int[] docIds)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.WriteHeader(writer, CodecConstants.PostingsVersion);

        writer.Write(term.Length);
        writer.Write(term.ToCharArray());
        writer.Write(docIds.Length);

        int prev = 0;
        for (int i = 0; i < docIds.Length; i++)
        {
            WriteVarInt(writer, docIds[i] - prev);
            prev = docIds[i];
        }
    }

    /// <summary>Writes a non-negative integer using variable-length encoding (LEB128).</summary>
    public static void WriteVarInt(BinaryWriter writer, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            writer.Write((byte)(v | 0x80));
            v >>= 7;
        }
        writer.Write((byte)v);
    }
}

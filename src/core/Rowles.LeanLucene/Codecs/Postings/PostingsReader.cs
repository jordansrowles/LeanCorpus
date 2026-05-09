namespace Rowles.LeanLucene.Codecs.Postings;

/// <summary>
/// Reads delta-encoded postings lists written by <see cref="PostingsWriter"/>.
/// </summary>
internal static class PostingsReader
{
    public static int[] ReadDocIds(string filePath, string term)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.ValidateHeader(reader, CodecConstants.PostingsVersion, "postings (.pos)");

        int termLen = reader.ReadInt32();
        var chars = reader.ReadChars(termLen);
        string storedTerm = new string(chars);

        if (!string.Equals(storedTerm, term, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected term '{term}' but found '{storedTerm}'.");

        int count = reader.ReadInt32();
        var docIds = new int[count];

        int prev = 0;
        for (int i = 0; i < count; i++)
        {
            int delta = ReadVarInt(reader);
            if (delta < 0)
                throw new InvalidDataException("Postings data is corrupt: negative delta encountered.");
            try
            {
                prev = checked(prev + delta);
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException("Postings data is corrupt: doc ID delta overflow.", ex);
            }
            docIds[i] = prev;
        }

        return docIds;
    }

    /// <summary>Reads a variable-length encoded integer (LEB128).</summary>
    public static int ReadVarInt(BinaryReader reader)
    {
        uint result = 0;
        int shift = 0;
        byte b;
        do
        {
            b = reader.ReadByte();
            if (shift >= 35)
                throw new InvalidDataException("VarInt is too large or malformed.");
            result |= (uint)(b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);

        if (result > int.MaxValue)
            throw new InvalidDataException("VarInt exceeds Int32 range.");
        return (int)result;
    }
}

using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Util;

namespace Rowles.LeanLucene.Codecs.DocValues;

/// <summary>
/// Reads per-document string values from a column-stride .dvs file.
/// Returns the dense value arrays alongside per-field presence bitmaps (v2 files only).
/// A null presence entry means all documents carry a value for that field.
/// </summary>
internal static class SortedDocValuesReader
{
    public static (Dictionary<string, string[]> Values, Dictionary<string, RoaringBitmap?> Presence) Read(string filePath)
    {
        var values = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var presence = new Dictionary<string, RoaringBitmap?>(StringComparer.Ordinal);

        if (!File.Exists(filePath)) return (values, presence);

        using var input = new IndexInput(filePath);

        byte version = CodecConstants.ReadHeaderVersion(input, CodecConstants.SortedDocValuesVersion, "sorted doc values (.dvs)");

        int fieldCount = input.ReadInt32();

        for (int f = 0; f < fieldCount; f++)
        {
            int nameLen = input.ReadVarInt();
            var nameBytes = new byte[nameLen];
            for (int b = 0; b < nameLen; b++)
                nameBytes[b] = input.ReadByte();
            string fieldName = System.Text.Encoding.UTF8.GetString(nameBytes);

            // Presence block is only present in v2+ files.
            RoaringBitmap? fieldPresence = null;
            if (version >= 2)
            {
                int presenceByteCount = input.ReadInt32();
                if (presenceByteCount > 0)
                {
                    var bitmapBytes = input.ReadBytes(presenceByteCount);
                    using var ms = new System.IO.MemoryStream(bitmapBytes);
                    using var br = new System.IO.BinaryReader(ms);
                    fieldPresence = RoaringBitmap.Deserialise(br);
                }
            }
            presence[fieldName] = fieldPresence;

            int docCount = input.ReadInt32();
            int ordCount = input.ReadInt32();

            var ordTable = new string[ordCount];
            for (int o = 0; o < ordCount; o++)
            {
                int len = input.ReadVarInt();
                var bytes = new byte[len];
                for (int b = 0; b < len; b++)
                    bytes[b] = input.ReadByte();
                ordTable[o] = System.Text.Encoding.UTF8.GetString(bytes);
            }

            int bitsPerOrd = input.ReadByte();
            var fieldValues = new string[docCount];

            if (bitsPerOrd == 0)
            {
                Array.Fill(fieldValues, ordTable.Length > 0 ? ordTable[0] : string.Empty);
            }
            else
            {
                ulong mask = (1UL << bitsPerOrd) - 1;
                ulong buffer = 0;
                int bitsInBuffer = 0;
                for (int i = 0; i < docCount; i++)
                {
                    while (bitsInBuffer < bitsPerOrd)
                    {
                        buffer |= (ulong)input.ReadByte() << bitsInBuffer;
                        bitsInBuffer += 8;
                    }
                    int ord = (int)(buffer & mask);
                    buffer >>= bitsPerOrd;
                    bitsInBuffer -= bitsPerOrd;
                    fieldValues[i] = ordTable[ord];
                }
            }

            values[fieldName] = fieldValues;
        }

        return (values, presence);
    }
}

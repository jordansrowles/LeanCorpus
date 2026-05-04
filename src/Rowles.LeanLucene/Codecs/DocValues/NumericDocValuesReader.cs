using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Util;

namespace Rowles.LeanLucene.Codecs.DocValues;

/// <summary>
/// Reads per-document numeric values from a column-stride .dvn file.
/// Returns the dense value arrays alongside per-field presence bitmaps (v2 files only).
/// A null presence entry means all documents carry a value for that field.
/// </summary>
internal static class NumericDocValuesReader
{
    public static (Dictionary<string, double[]> Values, Dictionary<string, RoaringBitmap?> Presence) Read(string filePath)
    {
        var values = new Dictionary<string, double[]>(StringComparer.Ordinal);
        var presence = new Dictionary<string, RoaringBitmap?>(StringComparer.Ordinal);

        if (!File.Exists(filePath)) return (values, presence);

        using var input = new IndexInput(filePath);

        byte version = CodecConstants.ReadHeaderVersion(input, CodecConstants.NumericDocValuesVersion, "numeric doc values (.dvn)");

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
                // presenceByteCount == 0 means all docs present; fieldPresence stays null.
            }
            presence[fieldName] = fieldPresence;

            int docCount = input.ReadInt32();
            long min = input.ReadInt64();
            int bitsPerValue = input.ReadByte();

            var fieldValues = new double[docCount];
            if (bitsPerValue == 0)
            {
                double constVal = BitConverter.Int64BitsToDouble(min);
                Array.Fill(fieldValues, constVal);
            }
            else
            {
                byte accum = 0;
                int accBits = 0;
                for (int i = 0; i < docCount; i++)
                {
                    ulong val = 0;
                    int collected = 0;
                    while (collected < bitsPerValue)
                    {
                        if (accBits == 0)
                        {
                            accum = input.ReadByte();
                            accBits = 8;
                        }
                        int take = Math.Min(bitsPerValue - collected, accBits);
                        val |= ((ulong)(accum & ((1 << take) - 1))) << collected;
                        accum >>= take;
                        accBits -= take;
                        collected += take;
                    }
                    fieldValues[i] = BitConverter.Int64BitsToDouble((long)((ulong)min + val));
                }
            }

            values[fieldName] = fieldValues;
        }

        return (values, presence);
    }
}

using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>
/// Reads per-document 64-bit integer values from a column-stride .dvnl file.
/// </summary>
internal static class Int64DocValuesReader
{
    public static (Dictionary<string, long[]> Values, Dictionary<string, RoaringBitmap?> Presence) Read(string filePath)
    {
        var values = new Dictionary<string, long[]>(StringComparer.Ordinal);
        var presence = new Dictionary<string, RoaringBitmap?>(StringComparer.Ordinal);

        if (!FileOpenRetry.FileExists(filePath)) return (values, presence);

        using var input = new IndexInput(filePath);

        byte version = CodecFileHeader.ReadVersion(input, CodecFormats.Int64DocValues);
        if (version > CodecConstants.Int64DocValuesVersion)
            throw new InvalidDataException(
                $"Unsupported Int64 doc values (.dvnl) format version {version}. " +
                $"This build supports up to v{CodecConstants.Int64DocValuesVersion}.");

        int fieldCount = input.ReadInt32();

        for (int f = 0; f < fieldCount; f++)
        {
            int nameLen = input.ReadVarInt();
            var nameBytes = new byte[nameLen];
            for (int b = 0; b < nameLen; b++)
                nameBytes[b] = input.ReadByte();
            string fieldName = System.Text.Encoding.UTF8.GetString(nameBytes);

            RoaringBitmap? fieldPresence = null;
            int presenceByteCount = input.ReadInt32();
            if (presenceByteCount > 0)
            {
                var bitmapBytes = input.ReadBytes(presenceByteCount);
                using var ms = new System.IO.MemoryStream(bitmapBytes);
                using var br = new System.IO.BinaryReader(ms);
                fieldPresence = RoaringBitmap.Deserialise(br);
            }
            presence[fieldName] = fieldPresence;

            int docCount = input.ReadInt32();
            long min = input.ReadInt64();
            int bitsPerValue = input.ReadByte();

            var fieldValues = new long[docCount];
            if (bitsPerValue == 0)
            {
                Array.Fill(fieldValues, min);
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
                    fieldValues[i] = (long)((ulong)min + val);
                }
            }

            values[fieldName] = fieldValues;
        }

        return (values, presence);
    }
}

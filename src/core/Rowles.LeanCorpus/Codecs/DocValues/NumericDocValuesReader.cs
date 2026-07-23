using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;
using System.Collections.Generic;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>
/// Reads per-document numeric values from a column-stride .dvn file.
/// Returns the dense value arrays alongside per-field presence bitmaps.
/// A null presence entry means all documents carry a value for that field.
/// </summary>
internal static class NumericDocValuesReader
{
    public static (Dictionary<string, double[]> Values, Dictionary<string, RoaringBitmap?> Presence) Read(string filePath)
    {
        var values = new Dictionary<string, double[]>(StringComparer.Ordinal);
        var presence = new Dictionary<string, RoaringBitmap?>(StringComparer.Ordinal);

        if (!FileOpenRetry.FileExists(filePath)) return (values, presence);

        using var input = new IndexInput(filePath);

        byte version = CodecFileHeader.ReadVersion(input, CodecFormats.NumericDocValues);
        if (version > CodecConstants.NumericDocValuesVersion)
            throw new InvalidDataException(
                $"Unsupported numeric doc values (.dvn) format version {version}. " +
                $"This build supports up to v{CodecConstants.NumericDocValuesVersion}.");

        int fieldCount = input.ReadInt32();

        for (int f = 0; f < fieldCount; f++)
        {
            int nameLen = input.ReadVarInt();
            var nameBytes = new byte[nameLen];
            for (int b = 0; b < nameLen; b++)
                nameBytes[b] = input.ReadByte();
            string fieldName = System.Text.Encoding.UTF8.GetString(nameBytes);

            // Presence block (current format)
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

            if ((uint)bitsPerValue > 64)
                throw new InvalidDataException(
                    $"Invalid bits-per-value {bitsPerValue} for numeric doc values field '{fieldName}'; must be between 0 and 64.");

            if (bitsPerValue > 0)
            {
                long expectedPackedBytes = ((long)bitsPerValue * docCount + 7) / 8;
                if (input.Position + expectedPackedBytes > input.Length)
                    throw new InvalidDataException(
                        $"Numeric doc values field '{fieldName}' declares {bitsPerValue} bits per value for {docCount} documents, but the file does not contain the expected packed data.");
            }

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

    internal static List<(string Name, double[] Values, RoaringBitmap? Presence)> EnumerateFields(string filePath)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return [];

        using var input = new IndexInput(filePath);

        byte version = CodecFileHeader.ReadVersionAndSkipHeader(input);
        if (version > CodecConstants.NumericDocValuesVersion)
            throw new InvalidDataException(
                $"Unsupported numeric doc values (.dvn) format version {version}. " +
                $"This build supports up to v{CodecConstants.NumericDocValuesVersion}.");

        int fieldCount = input.ReadInt32();
        var results = new List<(string Name, double[] Values, RoaringBitmap? Presence)>(fieldCount);

        for (int f = 0; f < fieldCount; f++)
        {
            int nameLen = input.ReadVarInt();
            var nameBytes = new byte[nameLen];
            for (int b = 0; b < nameLen; b++)
                nameBytes[b] = input.ReadByte();
            string fieldName = System.Text.Encoding.UTF8.GetString(nameBytes);

            // Presence block (current format)
            RoaringBitmap? fieldPresence = null;
            int presenceByteCount = input.ReadInt32();
            if (presenceByteCount > 0)
            {
                var bitmapBytes = input.ReadBytes(presenceByteCount);
                using var ms = new System.IO.MemoryStream(bitmapBytes);
                using var br = new System.IO.BinaryReader(ms);
                fieldPresence = RoaringBitmap.Deserialise(br);
            }

            int docCount = input.ReadInt32();
            long min = input.ReadInt64();
            int bitsPerValue = input.ReadByte();

            if ((uint)bitsPerValue > 64)
                throw new InvalidDataException(
                    $"Invalid bits-per-value {bitsPerValue} for numeric doc values field '{fieldName}'; must be between 0 and 64.");

            if (bitsPerValue > 0)
            {
                long expectedPackedBytes = ((long)bitsPerValue * docCount + 7) / 8;
                if (input.Position + expectedPackedBytes > input.Length)
                    throw new InvalidDataException(
                        $"Numeric doc values field '{fieldName}' declares {bitsPerValue} bits per value for {docCount} documents, but the file does not contain the expected packed data.");
            }

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

            results.Add((fieldName, fieldValues, fieldPresence));
        }

        return results;
    }
}

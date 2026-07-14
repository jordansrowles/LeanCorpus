using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;
using System.Collections.Generic;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>
/// Reads per-document string values from a column-stride .dvs file.
/// Returns the dense value arrays alongside per-field presence bitmaps.
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

        byte version = CodecFileHeader.ReadVersion(input, CodecFormats.SortedDocValues);

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
            if (bitsPerOrd > 63)
                throw new InvalidDataException(
                    $"Sorted DocValues field '{fieldName}' has bitsPerOrd={bitsPerOrd}, max is 63.");

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
                    if ((uint)ord >= (uint)ordTable.Length)
                        throw new InvalidDataException(
                            $"Sorted DocValues field '{fieldName}' has ordinal {ord} but ordTable has {ordTable.Length} entries.");
                    fieldValues[i] = ordTable[ord];
                }
            }

            values[fieldName] = fieldValues;
        }

        return (values, presence);
    }

    internal static List<(string Name, string?[] Values)> EnumerateFields(string filePath)
    {
        if (!File.Exists(filePath))
            return new List<(string, string?[])>(0);

        using var input = new IndexInput(filePath);

        byte version = CodecFileHeader.ReadVersionAndSkipHeader(input);

        int fieldCount = input.ReadInt32();

        var results = new List<(string Name, string?[] Values)>(fieldCount);

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
            if (bitsPerOrd > 63)
                throw new InvalidDataException(
                    $"Sorted DocValues field '{fieldName}' has bitsPerOrd={bitsPerOrd}, max is 63.");

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
                    if ((uint)ord >= (uint)ordTable.Length)
                        throw new InvalidDataException(
                            $"Sorted DocValues field '{fieldName}' has ordinal {ord} but ordTable has {ordTable.Length} entries.");
                    fieldValues[i] = ordTable[ord];
                }
            }

            results.Add((fieldName, fieldValues));
        }

        return results;
    }
}

using Rowles.LeanCorpus.Codecs.CodecKit;
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

        if (!FileOpenRetry.FileExists(filePath)) return (values, presence);

        using var input = new IndexInput(filePath);
        return Read(input);
    }

    internal static (Dictionary<string, string[]> Values, Dictionary<string, RoaringBitmap?> Presence) Read(IndexInput input)
    {
        using var inputLifetime = input;
        var values = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var presence = new Dictionary<string, RoaringBitmap?>(StringComparer.Ordinal);

        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.Sorted);

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

    internal static Dictionary<string, string[]> ReadTerms(string filePath)
    {
        var terms = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!FileOpenRetry.FileExists(filePath)) return terms;

        using var input = new IndexInput(filePath);
        return ReadTerms(input);
    }

    internal static Dictionary<string, string[]> ReadTerms(IndexInput input)
    {
        using var inputLifetime = input;
        var terms = new Dictionary<string, string[]>(StringComparer.Ordinal);
        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.Sorted);
        int fieldCount = input.ReadInt32();
        for (int f = 0; f < fieldCount; f++)
        {
            string fieldName = ReadString(input);
            int presenceByteCount = input.ReadInt32();
            if (presenceByteCount < 0)
                throw new InvalidDataException($"Sorted DocValues field '{fieldName}' has a negative presence length.");
            input.Seek(checked(input.Position + presenceByteCount));

            int docCount = input.ReadInt32();
            int ordCount = input.ReadInt32();
            if (docCount < 0 || ordCount < 0)
                throw new InvalidDataException($"Sorted DocValues field '{fieldName}' has a negative count.");
            var fieldTerms = new string[ordCount];
            for (int ordinal = 0; ordinal < ordCount; ordinal++)
                fieldTerms[ordinal] = ReadString(input);

            int bitsPerOrd = input.ReadByte();
            if (bitsPerOrd > 63)
                throw new InvalidDataException(
                    $"Sorted DocValues field '{fieldName}' has bitsPerOrd={bitsPerOrd}, max is 63.");
            long packedByteCount = ((long)docCount * bitsPerOrd + 7) / 8;
            input.Seek(checked(input.Position + packedByteCount));
            terms[fieldName] = fieldTerms;
        }

        return terms;
    }

    internal static List<(string Name, string?[] Values)> EnumerateFields(string filePath)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return new List<(string, string?[])>(0);

        using var input = new IndexInput(filePath);

        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.Sorted);

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

            var fieldValues = new string?[docCount];

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

            if (fieldPresence is not null)
            {
                for (int docId = 0; docId < fieldValues.Length; docId++)
                {
                    if (!fieldPresence.Contains(docId))
                        fieldValues[docId] = null;
                }
            }

            results.Add((fieldName, fieldValues));
        }

        return results;
    }

    private static string ReadString(IndexInput input)
    {
        int length = input.ReadVarInt();
        if (length < 0)
            throw new InvalidDataException("Negative string length in sorted DocValues.");
        return System.Text.Encoding.UTF8.GetString(input.ReadBytes(length));
    }
}

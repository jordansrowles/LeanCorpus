using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;
using System.Collections.Generic;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>
/// Reads multi-valued string DocValues from a .dss sidecar file.
/// </summary>
internal static class SortedSetDocValuesReader
{
    public static Dictionary<string, string[][]> Read(string filePath)
    {
        var values = new Dictionary<string, string[][]>(StringComparer.Ordinal);
        if (!File.Exists(filePath))
            return values;

        using var input = new IndexInput(filePath);
        byte version = CodecFileHeader.ReadVersion(input, CodecFormats.SortedSetDocValues);

        int fieldCount = input.ReadInt32();
        for (int f = 0; f < fieldCount; f++)
        {
            string fieldName = ReadString(input);
            int docCount = input.ReadInt32();
            int ordCount = input.ReadInt32();
            var ordTable = new string[ordCount];
            for (int i = 0; i < ordTable.Length; i++)
                ordTable[i] = ReadString(input);

            var starts = new int[docCount + 1];
            for (int i = 0; i < starts.Length; i++)
                starts[i] = input.ReadInt32();

            int totalOrdinals = input.ReadInt32();
            ValidateStarts(starts, totalOrdinals, fieldName);

            var ordinals = new int[totalOrdinals];
            for (int i = 0; i < ordinals.Length; i++)
            {
                int ord = input.ReadVarInt();
                if ((uint)ord >= (uint)ordTable.Length)
                    throw new InvalidDataException($"Invalid sorted-set DocValues ordinal {ord} for field '{fieldName}'.");
                ordinals[i] = ord;
            }

            var perDoc = new string[docCount][];
            for (int docId = 0; docId < docCount; docId++)
            {
                int start = starts[docId];
                int end = starts[docId + 1];
                if (end == start)
                {
                    perDoc[docId] = [];
                    continue;
                }

                var docValues = new string[end - start];
                for (int i = 0; i < docValues.Length; i++)
                    docValues[i] = ordTable[ordinals[start + i]];
                perDoc[docId] = docValues;
            }

            values[fieldName] = perDoc;
        }

        return values;
    }

    internal static List<(string Name, IReadOnlyList<string>?[] Values)> EnumerateFields(string filePath)
    {
        if (!File.Exists(filePath))
            return new List<(string, IReadOnlyList<string>?[])>(0);

        using var input = new IndexInput(filePath);
        byte version = CodecFileHeader.ReadVersionAndSkipHeader(input);

        int fieldCount = input.ReadInt32();
        var results = new List<(string, IReadOnlyList<string>?[])>(fieldCount);
        for (int f = 0; f < fieldCount; f++)
        {
            string fieldName = ReadString(input);
            int docCount = input.ReadInt32();
            int ordCount = input.ReadInt32();
            var ordTable = new string[ordCount];
            for (int i = 0; i < ordTable.Length; i++)
                ordTable[i] = ReadString(input);

            var starts = new int[docCount + 1];
            for (int i = 0; i < starts.Length; i++)
                starts[i] = input.ReadInt32();

            int totalOrdinals = input.ReadInt32();
            ValidateStarts(starts, totalOrdinals, fieldName);

            var ordinals = new int[totalOrdinals];
            for (int i = 0; i < ordinals.Length; i++)
            {
                int ord = input.ReadVarInt();
                if ((uint)ord >= (uint)ordTable.Length)
                    throw new InvalidDataException($"Invalid sorted-set DocValues ordinal {ord} for field '{fieldName}'.");
                ordinals[i] = ord;
            }

            var perDoc = new IReadOnlyList<string>[docCount];
            for (int docId = 0; docId < docCount; docId++)
            {
                int start = starts[docId];
                int end = starts[docId + 1];
                if (end == start)
                {
                    perDoc[docId] = Array.Empty<string>();
                    continue;
                }

                var docValues = new string[end - start];
                for (int i = 0; i < docValues.Length; i++)
                    docValues[i] = ordTable[ordinals[start + i]];
                perDoc[docId] = docValues;
            }

            results.Add((fieldName, perDoc));
        }

        return results;
    }

    private static void ValidateStarts(int[] starts, int totalValues, string fieldName)
    {
        if (starts[0] != 0)
            throw new InvalidDataException($"Invalid sorted-set DocValues offsets for field '{fieldName}'.");

        int previous = 0;
        for (int i = 0; i < starts.Length; i++)
        {
            int current = starts[i];
            if (current < previous || current > totalValues)
                throw new InvalidDataException($"Invalid sorted-set DocValues offsets for field '{fieldName}'.");
            previous = current;
        }

        if (starts[^1] != totalValues)
            throw new InvalidDataException($"Invalid sorted-set DocValues terminal offset for field '{fieldName}'.");
    }

    private static string ReadString(IndexInput input)
    {
        int length = input.ReadVarInt();
        if (length < 0)
            throw new InvalidDataException("Negative string length in sorted-set DocValues.");
        return Encoding.UTF8.GetString(input.ReadBytes(length));
    }
}

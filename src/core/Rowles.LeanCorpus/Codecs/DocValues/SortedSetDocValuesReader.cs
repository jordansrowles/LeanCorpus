using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>Opens sorted-set DocValues as term tables and flat local ordinals.</summary>
internal static class SortedSetDocValuesReader
{
    public static Dictionary<string, string[][]> Read(string filePath)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return new Dictionary<string, string[][]>(StringComparer.Ordinal);

        using var input = new IndexInput(filePath);
        return Read(input);
    }

    internal static Dictionary<string, string[][]> Read(IndexInput input)
    {
        using (input)
        {
            var columns = OpenColumns(input);
            var values = new Dictionary<string, string[][]>(columns.Count, StringComparer.Ordinal);
            foreach ((string field, SortedSetDocValuesColumn column) in columns)
                values.Add(field, column.Materialise());
            return values;
        }
    }

    /// <summary>
    /// Parses each term table and flat ordinal vector once. The caller owns <paramref name="input"/>
    /// for the lifetime of the returned columns.
    /// </summary>
    internal static Dictionary<string, SortedSetDocValuesColumn> OpenColumns(IndexInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var columns = new Dictionary<string, SortedSetDocValuesColumn>(StringComparer.Ordinal);
        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.SortedSet);

        int fieldCount = input.ReadInt32();
        if (fieldCount < 0)
            throw new InvalidDataException("Sorted-set DocValues field count cannot be negative.");

        long bodyEnd = checked(frame.BodyStart + frame.BodyLength);
        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            string fieldName = ReadString(input);
            int documentCount = input.ReadInt32();
            int ordinalCount = input.ReadInt32();
            if (documentCount < 0 || ordinalCount < 0)
                throw new InvalidDataException($"Sorted-set DocValues field '{fieldName}' has a negative count.");

            var terms = new string[ordinalCount];
            for (int ordinal = 0; ordinal < terms.Length; ordinal++)
                terms[ordinal] = ReadString(input);

            var documentStarts = new int[checked(documentCount + 1)];
            for (int index = 0; index < documentStarts.Length; index++)
                documentStarts[index] = input.ReadInt32();

            int valueCount = input.ReadInt32();
            if (valueCount < 0)
                throw new InvalidDataException($"Sorted-set DocValues field '{fieldName}' has a negative ordinal count.");
            ValidateStarts(documentStarts, valueCount, fieldName);

            long remainingBody = bodyEnd - input.Position;
            if (remainingBody < valueCount)
                throw new InvalidDataException($"Sorted-set DocValues field '{fieldName}' has a truncated ordinal vector.");

            var ordinals = new int[valueCount];
            for (int index = 0; index < ordinals.Length; index++)
            {
                int ordinal = input.ReadVarInt();
                if ((uint)ordinal >= (uint)terms.Length)
                    throw new InvalidDataException(
                        $"Invalid sorted-set DocValues ordinal {ordinal} for field '{fieldName}'.");
                ordinals[index] = ordinal;
            }

            columns.Add(fieldName, new SortedSetDocValuesColumn(terms, documentStarts, ordinals));
        }

        frame.ValidateChecksum();
        return columns;
    }

    internal static Dictionary<string, string[]> ReadTerms(string filePath)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return new Dictionary<string, string[]>(StringComparer.Ordinal);

        using var input = new IndexInput(filePath);
        return ReadTerms(input);
    }

    internal static Dictionary<string, string[]> ReadTerms(IndexInput input)
    {
        using (input)
        {
            var columns = OpenColumns(input);
            var terms = new Dictionary<string, string[]>(columns.Count, StringComparer.Ordinal);
            foreach ((string field, SortedSetDocValuesColumn column) in columns)
                terms.Add(field, column.CopyTerms());
            return terms;
        }
    }

    internal static List<(string Name, IReadOnlyList<string>?[] Values)> EnumerateFields(string filePath)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return [];

        var values = Read(filePath);
        var result = new List<(string, IReadOnlyList<string>?[])>(values.Count);
        foreach ((string field, string[][] documents) in values)
        {
            var rows = new IReadOnlyList<string>?[documents.Length];
            for (int documentId = 0; documentId < documents.Length; documentId++)
                rows[documentId] = documents[documentId];
            result.Add((field, rows));
        }

        return result;
    }

    private static void ValidateStarts(int[] starts, int totalValues, string fieldName)
    {
        if (starts.Length == 0 || starts[0] != 0)
            throw new InvalidDataException($"Invalid sorted-set DocValues offsets for field '{fieldName}'.");

        int previous = 0;
        for (int index = 0; index < starts.Length; index++)
        {
            int current = starts[index];
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

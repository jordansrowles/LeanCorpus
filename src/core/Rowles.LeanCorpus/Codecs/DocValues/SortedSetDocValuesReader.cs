using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>Opens sorted-set DocValues as term tables and flat local ordinals.</summary>
internal static class SortedSetDocValuesReader
{
    public static Dictionary<string, string[][]> Read(string filePath, int? expectedDocumentCount = null)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return new Dictionary<string, string[][]>(StringComparer.Ordinal);

        using var input = new IndexInput(filePath);
        return Read(input, expectedDocumentCount);
    }

    internal static Dictionary<string, string[][]> Read(IndexInput input, int? expectedDocumentCount = null)
    {
        using (input)
        {
            var columns = OpenColumns(input, expectedDocumentCount);
            var values = new Dictionary<string, string[][]>(columns.Count, StringComparer.Ordinal);
            foreach ((string field, SortedSetDocValuesColumn column) in columns)
                values.Add(field, column.Materialise());
            return values;
        }
    }

    /// <summary>
    /// Parses and validates each term table and flat ordinal vector once. The caller owns
    /// <paramref name="input"/> for the lifetime of the returned columns.
    /// </summary>
    internal static Dictionary<string, SortedSetDocValuesColumn> OpenColumns(
        IndexInput input,
        int? expectedDocumentCount = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.SortedSet);
        var body = new DocValuesBodyReader(input, frame, DocValuesCodecFiles.SortedSet, expectedDocumentCount);
        int fieldCount = body.ReadFieldCount();
        var columns = new Dictionary<string, SortedSetDocValuesColumn>(StringComparer.Ordinal);

        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            string fieldName = body.ReadString(fieldName: null);
            int documentCount = body.ReadDocumentCount(fieldName);
            int ordinalCount = body.ReadCount("ordinal count", fieldName);
            string[] terms = body.ReadStringArray(ordinalCount, "term table", fieldName);

            int[] documentStarts = body.ReadInt32Array(
                documentCount,
                "document offsets",
                fieldName,
                includeTerminalValue: true);
            int valueCount = body.ReadCount("ordinal value count", fieldName);
            ValidateStarts(documentStarts, valueCount, fieldName, body);
            body.EnsureBodyBytes(valueCount, "minimum ordinal vector", fieldName);
            var ordinals = new int[valueCount];
            for (int index = 0; index < ordinals.Length; index++)
            {
                int ordinal = body.ReadVarInt("ordinal", fieldName);
                if ((uint)ordinal >= (uint)terms.Length)
                    throw body.Corruption(
                        $"Ordinal {ordinal} is outside the {terms.Length}-term table.",
                        fieldName);
                ordinals[index] = ordinal;
            }

            var column = new SortedSetDocValuesColumn(terms, documentStarts, ordinals);
            if (!columns.TryAdd(fieldName, column))
                throw body.Corruption("Field name is duplicated.", fieldName);
        }

        body.ValidateEnd();
        frame.ValidateChecksum();
        return columns;
    }

    internal static Dictionary<string, string[]> ReadTerms(string filePath, int? expectedDocumentCount = null)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return new Dictionary<string, string[]>(StringComparer.Ordinal);

        using var input = new IndexInput(filePath);
        return ReadTerms(input, expectedDocumentCount);
    }

    internal static Dictionary<string, string[]> ReadTerms(IndexInput input, int? expectedDocumentCount = null)
    {
        using (input)
        {
            var columns = OpenColumns(input, expectedDocumentCount);
            var terms = new Dictionary<string, string[]>(columns.Count, StringComparer.Ordinal);
            foreach ((string field, SortedSetDocValuesColumn column) in columns)
                terms.Add(field, column.CopyTerms());
            return terms;
        }
    }

    internal static List<(string Name, IReadOnlyList<string>?[] Values)> EnumerateFields(
        string filePath,
        int? expectedDocumentCount = null)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return [];

        var values = Read(filePath, expectedDocumentCount);
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

    private static void ValidateStarts(
        int[] starts,
        int totalValues,
        string fieldName,
        DocValuesBodyReader body)
    {
        if (starts.Length == 0 || starts[0] != 0)
            throw body.Corruption("Document offsets do not begin at zero.", fieldName);

        int previous = 0;
        foreach (int current in starts)
        {
            if (current < previous || current > totalValues)
                throw body.Corruption("Document offsets are not monotonic or exceed the value count.", fieldName);
            previous = current;
        }

        if (starts[^1] != totalValues)
            throw body.Corruption("Terminal document offset does not match the ordinal value count.", fieldName);
    }
}

using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>Opens sorted numeric DocValues as offsets and packed values.</summary>
internal static class SortedNumericDocValuesReader
{
    public static Dictionary<string, double[][]> Read(string filePath, int? expectedDocumentCount = null)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return new Dictionary<string, double[][]>(StringComparer.Ordinal);

        using var input = new IndexInput(filePath);
        return Read(input, expectedDocumentCount);
    }

    internal static Dictionary<string, double[][]> Read(IndexInput input, int? expectedDocumentCount = null)
    {
        using (input)
        {
            var columns = OpenColumns(input, expectedDocumentCount);
            var values = new Dictionary<string, double[][]>(columns.Count, StringComparer.Ordinal);
            foreach ((string field, SortedNumericDocValuesColumn column) in columns)
                values.Add(field, column.Materialise());
            return values;
        }
    }

    /// <summary>
    /// Parses and validates per-document offsets and packed-value positions without creating a jagged
    /// row array. The caller owns <paramref name="input"/> for the returned columns.
    /// </summary>
    internal static Dictionary<string, SortedNumericDocValuesColumn> OpenColumns(
        IndexInput input,
        int? expectedDocumentCount = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.SortedNumeric);
        var body = new DocValuesBodyReader(input, frame, DocValuesCodecFiles.SortedNumeric, expectedDocumentCount);
        int fieldCount = body.ReadFieldCount();
        var columns = new Dictionary<string, SortedNumericDocValuesColumn>(StringComparer.Ordinal);

        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            string fieldName = body.ReadString(fieldName: null);
            int documentCount = body.ReadDocumentCount(fieldName);
            int[] documentStarts = body.ReadInt32Array(
                documentCount,
                "document offsets",
                fieldName,
                includeTerminalValue: true);

            int valueCount = body.ReadCount("value count", fieldName);
            ValidateStarts(documentStarts, valueCount, fieldName, body);
            long minimumBits = body.ReadInt64("minimum value", fieldName);
            int bitsPerValue = body.ReadByte("bits-per-value", fieldName);
            if (bitsPerValue > 64)
                throw body.Corruption(
                    $"Bits-per-value {bitsPerValue} must be between 0 and 64.",
                    fieldName);

            long packedByteCount = body.ReadPackedByteCount(valueCount, bitsPerValue, fieldName);
            long packedDataOffset = body.Position;
            body.EnsureBodyBytes(packedByteCount, "packed values", fieldName);
            var column = new SortedNumericDocValuesColumn(
                input, documentStarts, minimumBits, bitsPerValue, packedDataOffset);
            if (!columns.TryAdd(fieldName, column))
                throw body.Corruption("Field name is duplicated.", fieldName);
            body.Seek(checked(packedDataOffset + packedByteCount), "packed values", fieldName);
        }

        body.ValidateEnd();
        frame.ValidateChecksum();
        return columns;
    }

    internal static List<(string Name, IReadOnlyList<double>?[] Values)> EnumerateFields(
        string filePath,
        int? expectedDocumentCount = null)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return [];

        var values = Read(filePath, expectedDocumentCount);
        var results = new List<(string, IReadOnlyList<double>?[])>(values.Count);
        foreach ((string field, double[][] documents) in values)
        {
            var rows = new IReadOnlyList<double>?[documents.Length];
            for (int documentId = 0; documentId < documents.Length; documentId++)
                rows[documentId] = documents[documentId];
            results.Add((field, rows));
        }

        return results;
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
            throw body.Corruption("Terminal document offset does not match the value count.", fieldName);
    }
}

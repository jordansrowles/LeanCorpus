using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>Opens sorted Int64 DocValues as offsets and packed values.</summary>
internal static class Int64SortedNumericDocValuesReader
{
    public static Dictionary<string, long[][]> Read(string filePath, int? expectedDocumentCount = null)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return new Dictionary<string, long[][]>(StringComparer.Ordinal);

        using var input = new IndexInput(filePath);
        return Read(input, expectedDocumentCount);
    }

    internal static Dictionary<string, long[][]> Read(IndexInput input, int? expectedDocumentCount = null)
    {
        using (input)
        {
            var columns = OpenColumns(input, expectedDocumentCount);
            var values = new Dictionary<string, long[][]>(columns.Count, StringComparer.Ordinal);
            foreach ((string field, Int64SortedNumericDocValuesColumn column) in columns)
                values.Add(field, column.Materialise());
            return values;
        }
    }

    /// <summary>
    /// Parses and validates per-document offsets and packed-value positions without creating a jagged
    /// row array. The caller owns <paramref name="input"/> for the returned columns.
    /// </summary>
    internal static Dictionary<string, Int64SortedNumericDocValuesColumn> OpenColumns(
        IndexInput input,
        int? expectedDocumentCount = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.Int64SortedNumeric);
        var body = new DocValuesBodyReader(input, frame, DocValuesCodecFiles.Int64SortedNumeric, expectedDocumentCount);
        int fieldCount = body.ReadFieldCount();
        var columns = new Dictionary<string, Int64SortedNumericDocValuesColumn>(StringComparer.Ordinal);

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
            long minimum = body.ReadInt64("minimum value", fieldName);
            int bitsPerValue = body.ReadByte("bits-per-value", fieldName);
            if (bitsPerValue > 64)
                throw body.Corruption(
                    $"Bits-per-value {bitsPerValue} must be between 0 and 64.",
                    fieldName);

            long packedByteCount = body.ReadPackedByteCount(valueCount, bitsPerValue, fieldName);
            long packedDataOffset = body.Position;
            body.EnsureBodyBytes(packedByteCount, "packed values", fieldName);
            var column = new Int64SortedNumericDocValuesColumn(
                input, documentStarts, minimum, bitsPerValue, packedDataOffset);
            if (!columns.TryAdd(fieldName, column))
                throw body.Corruption("Field name is duplicated.", fieldName);
            body.Seek(checked(packedDataOffset + packedByteCount), "packed values", fieldName);
        }

        body.ValidateEnd();
        frame.ValidateChecksum();
        return columns;
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

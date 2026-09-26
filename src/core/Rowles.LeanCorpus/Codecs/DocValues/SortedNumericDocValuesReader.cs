using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>Opens sorted numeric DocValues as offsets and packed values.</summary>
internal static class SortedNumericDocValuesReader
{
    public static Dictionary<string, double[][]> Read(string filePath)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return new Dictionary<string, double[][]>(StringComparer.Ordinal);

        using var input = new IndexInput(filePath);
        return Read(input);
    }

    internal static Dictionary<string, double[][]> Read(IndexInput input)
    {
        using (input)
        {
            var columns = OpenColumns(input);
            var values = new Dictionary<string, double[][]>(columns.Count, StringComparer.Ordinal);
            foreach ((string field, SortedNumericDocValuesColumn column) in columns)
                values.Add(field, column.Materialise());
            return values;
        }
    }

    /// <summary>
    /// Parses per-document offsets and packed-value positions without creating a jagged row array.
    /// The caller owns <paramref name="input"/> for the lifetime of the returned columns.
    /// </summary>
    internal static Dictionary<string, SortedNumericDocValuesColumn> OpenColumns(IndexInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var columns = new Dictionary<string, SortedNumericDocValuesColumn>(StringComparer.Ordinal);
        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.SortedNumeric);

        int fieldCount = input.ReadInt32();
        if (fieldCount < 0)
            throw new InvalidDataException("Sorted-numeric DocValues field count cannot be negative.");

        long bodyEnd = checked(frame.BodyStart + frame.BodyLength);
        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            string fieldName = ReadString(input);
            int documentCount = input.ReadInt32();
            if (documentCount < 0)
                throw new InvalidDataException($"Sorted-numeric DocValues field '{fieldName}' has a negative document count.");

            var documentStarts = new int[checked(documentCount + 1)];
            for (int index = 0; index < documentStarts.Length; index++)
                documentStarts[index] = input.ReadInt32();

            int valueCount = input.ReadInt32();
            if (valueCount < 0)
                throw new InvalidDataException($"Sorted-numeric DocValues field '{fieldName}' has a negative value count.");
            ValidateStarts(documentStarts, valueCount, fieldName);

            long minimumBits = input.ReadInt64();
            int bitsPerValue = input.ReadByte();
            if ((uint)bitsPerValue > 64)
                throw new InvalidDataException(
                    $"Invalid bits-per-value {bitsPerValue} for sorted-numeric DocValues field '{fieldName}'; must be between 0 and 64.");

            long packedByteCount = checked(((long)bitsPerValue * valueCount + 7) / 8);
            long packedDataOffset = input.Position;
            if (packedDataOffset > bodyEnd || packedByteCount > bodyEnd - packedDataOffset)
                throw new InvalidDataException(
                    $"Sorted-numeric DocValues field '{fieldName}' does not contain its declared packed values.");

            columns.Add(fieldName, new SortedNumericDocValuesColumn(
                input, documentStarts, minimumBits, bitsPerValue, packedDataOffset));
            input.Seek(checked(packedDataOffset + packedByteCount));
        }

        frame.ValidateChecksum();
        return columns;
    }

    internal static List<(string Name, IReadOnlyList<double>?[] Values)> EnumerateFields(string filePath)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return [];

        var values = Read(filePath);
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

    private static void ValidateStarts(int[] starts, int totalValues, string fieldName)
    {
        if (starts.Length == 0 || starts[0] != 0)
            throw new InvalidDataException($"Invalid sorted-numeric DocValues offsets for field '{fieldName}'.");

        int previous = 0;
        for (int index = 0; index < starts.Length; index++)
        {
            int current = starts[index];
            if (current < previous || current > totalValues)
                throw new InvalidDataException($"Invalid sorted-numeric DocValues offsets for field '{fieldName}'.");
            previous = current;
        }

        if (starts[^1] != totalValues)
            throw new InvalidDataException($"Invalid sorted-numeric DocValues terminal offset for field '{fieldName}'.");
    }

    private static string ReadString(IndexInput input)
    {
        int length = input.ReadVarInt();
        if (length < 0)
            throw new InvalidDataException("Negative string length in sorted-numeric DocValues.");
        return Encoding.UTF8.GetString(input.ReadBytes(length));
    }
}

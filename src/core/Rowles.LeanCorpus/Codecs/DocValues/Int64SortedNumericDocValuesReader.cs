using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>Opens sorted Int64 DocValues as offsets and packed values.</summary>
internal static class Int64SortedNumericDocValuesReader
{
    public static Dictionary<string, long[][]> Read(string filePath)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return new Dictionary<string, long[][]>(StringComparer.Ordinal);

        using var input = new IndexInput(filePath);
        return Read(input);
    }

    internal static Dictionary<string, long[][]> Read(IndexInput input)
    {
        using (input)
        {
            var columns = OpenColumns(input);
            var values = new Dictionary<string, long[][]>(columns.Count, StringComparer.Ordinal);
            foreach ((string field, Int64SortedNumericDocValuesColumn column) in columns)
                values.Add(field, column.Materialise());
            return values;
        }
    }

    /// <summary>
    /// Parses per-document offsets and packed-value positions without creating a jagged row array.
    /// The caller owns <paramref name="input"/> for the lifetime of the returned columns.
    /// </summary>
    internal static Dictionary<string, Int64SortedNumericDocValuesColumn> OpenColumns(IndexInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var columns = new Dictionary<string, Int64SortedNumericDocValuesColumn>(StringComparer.Ordinal);
        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.Int64SortedNumeric);

        int fieldCount = input.ReadInt32();
        if (fieldCount < 0)
            throw new InvalidDataException("Sorted Int64 DocValues field count cannot be negative.");

        long bodyEnd = checked(frame.BodyStart + frame.BodyLength);
        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            string fieldName = ReadString(input);
            int documentCount = input.ReadInt32();
            if (documentCount < 0)
                throw new InvalidDataException($"Sorted Int64 DocValues field '{fieldName}' has a negative document count.");

            var documentStarts = new int[checked(documentCount + 1)];
            for (int index = 0; index < documentStarts.Length; index++)
                documentStarts[index] = input.ReadInt32();

            int valueCount = input.ReadInt32();
            if (valueCount < 0)
                throw new InvalidDataException($"Sorted Int64 DocValues field '{fieldName}' has a negative value count.");
            ValidateStarts(documentStarts, valueCount, fieldName);

            long minimum = input.ReadInt64();
            int bitsPerValue = input.ReadByte();
            if ((uint)bitsPerValue > 64)
                throw new InvalidDataException(
                    $"Invalid bits-per-value {bitsPerValue} for sorted Int64 DocValues field '{fieldName}'; must be between 0 and 64.");

            long packedByteCount = checked(((long)bitsPerValue * valueCount + 7) / 8);
            long packedDataOffset = input.Position;
            if (packedDataOffset > bodyEnd || packedByteCount > bodyEnd - packedDataOffset)
                throw new InvalidDataException(
                    $"Sorted Int64 DocValues field '{fieldName}' does not contain its declared packed values.");

            columns.Add(fieldName, new Int64SortedNumericDocValuesColumn(
                input, documentStarts, minimum, bitsPerValue, packedDataOffset));
            input.Seek(checked(packedDataOffset + packedByteCount));
        }

        frame.ValidateChecksum();
        return columns;
    }

    private static void ValidateStarts(int[] starts, int totalValues, string fieldName)
    {
        if (starts.Length == 0 || starts[0] != 0)
            throw new InvalidDataException($"Invalid sorted Int64 DocValues offsets for field '{fieldName}'.");

        int previous = 0;
        for (int index = 0; index < starts.Length; index++)
        {
            int current = starts[index];
            if (current < previous || current > totalValues)
                throw new InvalidDataException($"Invalid sorted Int64 DocValues offsets for field '{fieldName}'.");
            previous = current;
        }

        if (starts[^1] != totalValues)
            throw new InvalidDataException($"Invalid sorted Int64 DocValues terminal offset for field '{fieldName}'.");
    }

    private static string ReadString(IndexInput input)
    {
        int length = input.ReadVarInt();
        if (length < 0)
            throw new InvalidDataException("Negative string length in sorted Int64 DocValues.");
        return Encoding.UTF8.GetString(input.ReadBytes(length));
    }
}

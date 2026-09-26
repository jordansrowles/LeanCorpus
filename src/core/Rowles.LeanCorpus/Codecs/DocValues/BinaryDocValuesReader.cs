using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>Opens binary DocValues as mapped offsets into one shared payload range.</summary>
internal static class BinaryDocValuesReader
{
    public static Dictionary<string, byte[][][]> Read(string filePath)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return new Dictionary<string, byte[][][]>(StringComparer.Ordinal);

        using var input = new IndexInput(filePath);
        return Read(input);
    }

    internal static Dictionary<string, byte[][][]> Read(IndexInput input)
    {
        using (input)
        {
            var columns = OpenColumns(input);
            var values = new Dictionary<string, byte[][][]>(columns.Count, StringComparer.Ordinal);
            foreach ((string field, BinaryDocValuesColumn column) in columns)
                values.Add(field, column.Materialise());
            return values;
        }
    }

    /// <summary>
    /// Parses document and payload offsets without copying each binary value. The caller owns
    /// <paramref name="input"/> for the lifetime of the returned columns.
    /// </summary>
    internal static Dictionary<string, BinaryDocValuesColumn> OpenColumns(IndexInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var columns = new Dictionary<string, BinaryDocValuesColumn>(StringComparer.Ordinal);
        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.Binary);

        int fieldCount = input.ReadInt32();
        if (fieldCount < 0)
            throw new InvalidDataException("Binary DocValues field count cannot be negative.");

        long bodyEnd = checked(frame.BodyStart + frame.BodyLength);
        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            string fieldName = ReadString(input);
            int documentCount = input.ReadInt32();
            if (documentCount < 0)
                throw new InvalidDataException($"Binary DocValues field '{fieldName}' has a negative document count.");

            var documentStarts = new int[checked(documentCount + 1)];
            for (int index = 0; index < documentStarts.Length; index++)
                documentStarts[index] = input.ReadInt32();

            int valueCount = input.ReadInt32();
            if (valueCount < 0)
                throw new InvalidDataException($"Binary DocValues field '{fieldName}' has a negative value count.");
            ValidateStarts(documentStarts, valueCount, fieldName);

            var valueByteOffsets = new int[checked(valueCount + 1)];
            for (int index = 0; index < valueByteOffsets.Length; index++)
                valueByteOffsets[index] = input.ReadInt32();
            if (valueByteOffsets[0] != 0)
                throw new InvalidDataException($"Invalid binary DocValues byte offsets for field '{fieldName}'.");

            int previous = 0;
            for (int index = 0; index < valueByteOffsets.Length; index++)
            {
                int current = valueByteOffsets[index];
                if (current < previous)
                    throw new InvalidDataException($"Invalid binary DocValues byte offsets for field '{fieldName}'.");
                previous = current;
            }

            long payloadOffset = input.Position;
            int payloadLength = valueByteOffsets[^1];
            if (payloadOffset > bodyEnd || payloadLength > bodyEnd - payloadOffset)
                throw new InvalidDataException($"Binary DocValues field '{fieldName}' has a truncated payload.");

            columns.Add(fieldName, new BinaryDocValuesColumn(
                input, documentStarts, valueByteOffsets, payloadOffset));
            input.Seek(checked(payloadOffset + payloadLength));
        }

        frame.ValidateChecksum();
        return columns;
    }

    internal static List<(string Name, IReadOnlyList<byte[]>?[] Values)> EnumerateFields(string filePath)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return [];

        var values = Read(filePath);
        var results = new List<(string, IReadOnlyList<byte[]>?[])>(values.Count);
        foreach ((string field, byte[][][] documents) in values)
        {
            var rows = new IReadOnlyList<byte[]>?[documents.Length];
            for (int documentId = 0; documentId < documents.Length; documentId++)
                rows[documentId] = documents[documentId];
            results.Add((field, rows));
        }

        return results;
    }

    private static void ValidateStarts(int[] starts, int totalValues, string fieldName)
    {
        if (starts.Length == 0 || starts[0] != 0)
            throw new InvalidDataException($"Invalid binary DocValues offsets for field '{fieldName}'.");

        int previous = 0;
        for (int index = 0; index < starts.Length; index++)
        {
            int current = starts[index];
            if (current < previous || current > totalValues)
                throw new InvalidDataException($"Invalid binary DocValues offsets for field '{fieldName}'.");
            previous = current;
        }

        if (starts[^1] != totalValues)
            throw new InvalidDataException($"Invalid binary DocValues terminal offset for field '{fieldName}'.");
    }

    private static string ReadString(IndexInput input)
    {
        int length = input.ReadVarInt();
        if (length < 0)
            throw new InvalidDataException("Negative string length in binary DocValues.");
        return Encoding.UTF8.GetString(input.ReadBytes(length));
    }
}

using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>Opens binary DocValues as mapped offsets into one shared payload range.</summary>
internal static class BinaryDocValuesReader
{
    public static Dictionary<string, byte[][][]> Read(string filePath, int? expectedDocumentCount = null)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return new Dictionary<string, byte[][][]>(StringComparer.Ordinal);

        using var input = new IndexInput(filePath);
        return Read(input, expectedDocumentCount);
    }

    internal static Dictionary<string, byte[][][]> Read(IndexInput input, int? expectedDocumentCount = null)
    {
        using (input)
        {
            var columns = OpenColumns(input, expectedDocumentCount);
            var values = new Dictionary<string, byte[][][]>(columns.Count, StringComparer.Ordinal);
            foreach ((string field, BinaryDocValuesColumn column) in columns)
                values.Add(field, column.Materialise());
            return values;
        }
    }

    /// <summary>
    /// Parses and validates document and payload offsets without copying each binary value. The caller
    /// owns <paramref name="input"/> for the lifetime of the returned columns.
    /// </summary>
    internal static Dictionary<string, BinaryDocValuesColumn> OpenColumns(
        IndexInput input,
        int? expectedDocumentCount = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.Binary);
        var body = new DocValuesBodyReader(input, frame, DocValuesCodecFiles.Binary, expectedDocumentCount);
        int fieldCount = body.ReadFieldCount();
        var columns = new Dictionary<string, BinaryDocValuesColumn>(StringComparer.Ordinal);

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
            int[] valueByteOffsets = body.ReadInt32Array(
                valueCount,
                "value byte offsets",
                fieldName,
                includeTerminalValue: true);
            if (valueByteOffsets[0] != 0)
                throw body.Corruption("Byte offsets do not begin at zero.", fieldName);

            int previous = 0;
            foreach (int current in valueByteOffsets)
            {
                if (current < previous)
                    throw body.Corruption("Byte offsets are not monotonically increasing.", fieldName);
                previous = current;
            }

            long payloadOffset = body.Position;
            int payloadLength = valueByteOffsets[^1];
            body.EnsureBodyBytes(payloadLength, "binary payload", fieldName);
            var column = new BinaryDocValuesColumn(input, documentStarts, valueByteOffsets, payloadOffset);
            if (!columns.TryAdd(fieldName, column))
                throw body.Corruption("Field name is duplicated.", fieldName);

            body.Seek(checked(payloadOffset + payloadLength), "binary payload", fieldName);
        }

        body.ValidateEnd();
        frame.ValidateChecksum();
        return columns;
    }

    internal static List<(string Name, IReadOnlyList<byte[]>?[] Values)> EnumerateFields(
        string filePath,
        int? expectedDocumentCount = null)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return [];

        var values = Read(filePath, expectedDocumentCount);
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

    private static void ValidateStarts(int[] starts, int totalValues, string fieldName, DocValuesBodyReader body)
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

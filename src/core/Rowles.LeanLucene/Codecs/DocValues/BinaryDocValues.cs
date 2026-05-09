using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Codecs.DocValues;

/// <summary>
/// Writes multi-valued binary DocValues in a column-stride format (.dvb).
/// </summary>
internal static class BinaryDocValuesWriter
{
    public static void Write(
        string filePath,
        IReadOnlyDictionary<string, IReadOnlyList<byte[]>?[]> fields,
        int docCount,
        bool durable = false)
    {
        using var output = new IndexOutput(filePath, durable);
        CodecConstants.WriteHeader(output, CodecConstants.BinaryDocValuesVersion);
        output.WriteInt32(fields.Count);

        foreach (var (fieldName, values) in fields)
            WriteFieldBlock(output, fieldName, values, docCount);
    }

    internal static void WriteFieldBlock(
        IndexOutput output,
        string fieldName,
        IReadOnlyList<byte[]>?[] values,
        int docCount)
    {
        WriteString(output, fieldName);
        output.WriteInt32(docCount);

        var starts = new int[docCount + 1];
        var allValues = new List<byte[]>();
        for (int docId = 0; docId < docCount; docId++)
        {
            starts[docId] = allValues.Count;
            if ((uint)docId < (uint)values.Length && values[docId] is { Count: > 0 } source)
                allValues.AddRange(source);
        }
        starts[docCount] = allValues.Count;

        for (int i = 0; i < starts.Length; i++)
            output.WriteInt32(starts[i]);

        output.WriteInt32(allValues.Count);
        var byteOffsets = new int[allValues.Count + 1];
        int totalBytes = 0;
        for (int i = 0; i < allValues.Count; i++)
        {
            byteOffsets[i] = totalBytes;
            totalBytes += allValues[i].Length;
        }
        byteOffsets[^1] = totalBytes;

        for (int i = 0; i < byteOffsets.Length; i++)
            output.WriteInt32(byteOffsets[i]);

        foreach (var value in allValues)
            output.WriteBytes(value);
    }

    private static void WriteString(IndexOutput output, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        output.WriteVarInt(bytes.Length);
        output.WriteBytes(bytes);
    }
}

using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

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
        var bodyBuf = new ArrayBufferWriter<byte>(4096);
        bodyBuf.WriteInt32(fields.Count);

        foreach (var (fieldName, values) in fields)
            WriteFieldBlock(bodyBuf, fieldName, values, docCount);

        using var output = new IndexOutput(filePath, durable);
        CodecFileHeader.Write(output, CodecFormats.BinaryDocValues, bodyBuf.WrittenSpan);
    }

    internal static void WriteFieldBlock(
        IBufferWriter<byte> bw,
        string fieldName,
        IReadOnlyList<byte[]>?[] values,
        int docCount)
    {
        bw.WriteString(fieldName);
        bw.WriteInt32(docCount);

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
            bw.WriteInt32(starts[i]);

        bw.WriteInt32(allValues.Count);
        var byteOffsets = new int[allValues.Count + 1];
        int totalBytes = 0;
        for (int i = 0; i < allValues.Count; i++)
        {
            byteOffsets[i] = totalBytes;
            totalBytes += allValues[i].Length;
        }
        byteOffsets[^1] = totalBytes;

        for (int i = 0; i < byteOffsets.Length; i++)
            bw.WriteInt32(byteOffsets[i]);

        foreach (var value in allValues)
            bw.WriteBytes(value);
    }
}

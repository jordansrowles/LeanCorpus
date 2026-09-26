using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>
/// Writes multi-valued binary DocValues in a column-stride format (.dvb).
/// </summary>
internal static class BinaryDocValuesWriter
{
    internal const int MaximumFieldPayloadBytes = int.MaxValue - 1;

    public static void Write(
        string filePath,
        IReadOnlyDictionary<string, IReadOnlyList<byte[]>?[]> fields,
        int docCount,
        bool durable = false)
    {
        CodecFileWriter.WriteAtomically(filePath, DocValuesCodecFiles.Binary, durable, bodyOutput =>
        {
            bodyOutput.WriteInt32(fields.Count);
            foreach (var (fieldName, values) in fields)
                WriteFieldBlock(bodyOutput, fieldName, values, docCount);
        });
    }

    internal static void WriteFieldBlock(
        IBufferWriter<byte> bw,
        string fieldName,
        IReadOnlyList<byte[]>?[] values,
        int docCount)
    {
        var starts = new int[docCount + 1];
        var allValues = new List<byte[]>();
        for (int docId = 0; docId < docCount; docId++)
        {
            starts[docId] = allValues.Count;
            if ((uint)docId < (uint)values.Length && values[docId] is { Count: > 0 } source)
                allValues.AddRange(source);
        }
        starts[docCount] = allValues.Count;

        var byteOffsets = new int[allValues.Count + 1];
        long totalBytes = 0;
        for (int i = 0; i < allValues.Count; i++)
        {
            long nextTotalBytes = checked(totalBytes + allValues[i].Length);
            if (nextTotalBytes > MaximumFieldPayloadBytes)
            {
                throw new ArgumentException(
                    $"Binary DocValues field '{fieldName}' exceeds the maximum payload size of {MaximumFieldPayloadBytes} bytes.",
                    nameof(values));
            }

            byteOffsets[i] = checked((int)totalBytes);
            totalBytes = nextTotalBytes;
        }
        byteOffsets[^1] = checked((int)totalBytes);

        bw.WriteString(fieldName);
        bw.WriteInt32(docCount);

        for (int i = 0; i < starts.Length; i++)
            bw.WriteInt32(starts[i]);

        bw.WriteInt32(allValues.Count);

        for (int i = 0; i < byteOffsets.Length; i++)
            bw.WriteInt32(byteOffsets[i]);

        foreach (var value in allValues)
            bw.WriteBytes(value);
    }
}

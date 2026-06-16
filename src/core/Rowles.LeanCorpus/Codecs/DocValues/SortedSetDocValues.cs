using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>
/// Writes multi-valued string DocValues in a column-stride format (.dss).
/// </summary>
internal static class SortedSetDocValuesWriter
{
    public static void Write(
        string filePath,
        IReadOnlyDictionary<string, IReadOnlyList<string>?[]> fields,
        int docCount,
        bool durable = false)
    {
        var bodyBuf = new ArrayBufferWriter<byte>(4096);
        bodyBuf.WriteInt32(fields.Count);

        foreach (var (fieldName, values) in fields)
            WriteFieldBlock(bodyBuf, fieldName, values, docCount);

        using var output = new IndexOutput(filePath, durable);
        CodecFileHeader.Write(output, CodecFormats.SortedSetDocValues, bodyBuf.WrittenSpan);
    }

    internal static void WriteFieldBlock(
        IBufferWriter<byte> bw,
        string fieldName,
        IReadOnlyList<string>?[] values,
        int docCount)
    {
        bw.WriteString(fieldName);
        bw.WriteInt32(docCount);

        var ordSet = new SortedSet<string>(StringComparer.Ordinal);
        var perDoc = new string[docCount][];
        var starts = new int[docCount + 1];
        int totalOrdinals = 0;

        for (int docId = 0; docId < docCount; docId++)
        {
            starts[docId] = totalOrdinals;
            string[] docValues = [];
            if ((uint)docId < (uint)values.Length && values[docId] is { Count: > 0 } source)
            {
                docValues = source
                    .Where(static value => value is not null)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();

                foreach (var value in docValues)
                    ordSet.Add(value);
            }

            perDoc[docId] = docValues;
            totalOrdinals += docValues.Length;
        }
        starts[docCount] = totalOrdinals;

        var ordList = ordSet.ToArray();
        var ordMap = new Dictionary<string, int>(ordList.Length, StringComparer.Ordinal);
        for (int i = 0; i < ordList.Length; i++)
            ordMap[ordList[i]] = i;

        bw.WriteInt32(ordList.Length);
        foreach (var value in ordList)
            bw.WriteString(value);

        for (int i = 0; i < starts.Length; i++)
            bw.WriteInt32(starts[i]);

        bw.WriteInt32(totalOrdinals);
        foreach (var docValues in perDoc)
        {
            foreach (var value in docValues)
                bw.Write7BitEncodedInt(ordMap[value]);
        }
    }

}

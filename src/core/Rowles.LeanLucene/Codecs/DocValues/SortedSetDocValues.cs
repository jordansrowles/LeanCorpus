using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Codecs.DocValues;

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
        using var output = new IndexOutput(filePath, durable);
        CodecConstants.WriteHeader(output, CodecConstants.SortedSetDocValuesVersion);
        output.WriteInt32(fields.Count);

        foreach (var (fieldName, values) in fields)
            WriteFieldBlock(output, fieldName, values, docCount);
    }

    internal static void WriteFieldBlock(
        IndexOutput output,
        string fieldName,
        IReadOnlyList<string>?[] values,
        int docCount)
    {
        WriteFieldName(output, fieldName);
        output.WriteInt32(docCount);

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

        output.WriteInt32(ordList.Length);
        foreach (var value in ordList)
            WriteString(output, value);

        for (int i = 0; i < starts.Length; i++)
            output.WriteInt32(starts[i]);

        output.WriteInt32(totalOrdinals);
        foreach (var docValues in perDoc)
        {
            foreach (var value in docValues)
                output.WriteVarInt(ordMap[value]);
        }
    }

    private static void WriteFieldName(IndexOutput output, string fieldName)
        => WriteString(output, fieldName);

    private static void WriteString(IndexOutput output, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        output.WriteVarInt(bytes.Length);
        output.WriteBytes(bytes);
    }
}

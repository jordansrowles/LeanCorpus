using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Util;

namespace Rowles.LeanLucene.Codecs.DocValues;

/// <summary>
/// Writes per-document string values in a column-stride format (.dvs).
/// Layout (v2): [fieldName] [presenceByteCount: int32] [presenceBitmap: bytes if count > 0]
/// [docCount: int32] [ordCount: int32] [ord table: length-prefixed strings] [ords: packed ints].
/// Deduplicates values via an ordinal table. Null entries in the values array indicate absent docs.
/// </summary>
internal static class SortedDocValuesWriter
{
    public static void Write(string filePath, IReadOnlyDictionary<string, string?[]> fields, int docCount, bool durable = false)
    {
        using var output = new IndexOutput(filePath, durable);

        CodecConstants.WriteHeader(output, CodecConstants.SortedDocValuesVersion);

        output.WriteInt32(fields.Count);

        foreach (var (fieldName, values) in fields)
            WriteFieldBlock(output, fieldName, values, docCount);
    }

    /// <summary>
    /// Append a single field's column to an already-opened .dvs output. Used by the
    /// streaming merge path so columns can be released as they are flushed.
    /// </summary>
    internal static void WriteFieldBlock(IndexOutput output, string fieldName, string?[] values, int docCount)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(fieldName);
        output.WriteVarInt(nameBytes.Length);
        foreach (var b in nameBytes)
            output.WriteByte(b);

        // Presence bitmap: tracks which docs have an explicit (non-null) value.
        int presentCount = 0;
        for (int i = 0; i < docCount; i++)
            if (values[i] is not null) presentCount++;

        if (presentCount < docCount)
        {
            var bitmap = new RoaringBitmap();
            for (int i = 0; i < docCount; i++)
                if (values[i] is not null) bitmap.Add(i);
            using var ms = new System.IO.MemoryStream();
            using var bw = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            bitmap.Serialise(bw);
            bw.Flush();
            var bitmapBytes = ms.ToArray();
            output.WriteInt32(bitmapBytes.Length);
            output.WriteBytes(bitmapBytes);
        }
        else
        {
            output.WriteInt32(0); // all docs present
        }

        output.WriteInt32(docCount);

        var ordMap = new Dictionary<string, int>(StringComparer.Ordinal);
        var ordList = new List<string>();
        for (int i = 0; i < docCount; i++)
        {
            var v = values[i] ?? string.Empty;
            if (!ordMap.ContainsKey(v))
            {
                ordMap[v] = ordList.Count;
                ordList.Add(v);
            }
        }

        ordList.Sort(StringComparer.Ordinal);
        for (int i = 0; i < ordList.Count; i++)
            ordMap[ordList[i]] = i;

        output.WriteInt32(ordList.Count);
        foreach (var ord in ordList)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(ord);
            output.WriteVarInt(bytes.Length);
            foreach (var b in bytes)
                output.WriteByte(b);
        }

        int bitsPerOrd = ordList.Count <= 1 ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)(ordList.Count - 1));
        output.WriteByte((byte)bitsPerOrd);

        if (bitsPerOrd > 0)
        {
            ulong buffer = 0;
            int bitsInBuffer = 0;
            for (int i = 0; i < docCount; i++)
            {
                int ord = ordMap[values[i] ?? string.Empty];
                buffer |= (ulong)ord << bitsInBuffer;
                bitsInBuffer += bitsPerOrd;
                while (bitsInBuffer >= 8)
                {
                    output.WriteByte((byte)(buffer & 0xFF));
                    buffer >>= 8;
                    bitsInBuffer -= 8;
                }
            }
            if (bitsInBuffer > 0)
                output.WriteByte((byte)(buffer & 0xFF));
        }
    }
}

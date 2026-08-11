using System.Buffers;
using System.IO;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>
/// Writes per-document string values in a column-stride format (.dvs).
/// Layout: [fieldName] [presenceByteCount: int32] [presenceBitmap: bytes if count > 0]
/// [docCount: int32] [ordCount: int32] [ord table: length-prefixed strings] [ords: packed ints].
/// Deduplicates values via an ordinal table. Null entries in the values array indicate absent docs.
/// </summary>
internal static class SortedDocValuesWriter
{
    public static void Write(string filePath, IReadOnlyDictionary<string, string?[]> fields, int docCount, bool durable = false)
    {
        CodecFileWriter.WriteAtomically(filePath, DocValuesCodecFiles.Sorted, durable, bodyOutput =>
        {
            bodyOutput.WriteInt32(fields.Count);
            foreach (var (fieldName, values) in fields)
                WriteFieldBlock(bodyOutput, fieldName, values, docCount);
        });
    }

    internal static void WriteFieldBlock(IBufferWriter<byte> bw, string fieldName, string?[] values, int docCount)
    {
        bw.WriteString(fieldName);

        // Presence bitmap: tracks which docs have an explicit (non-null) value.
        int presentCount = 0;
        for (int i = 0; i < docCount; i++)
            if (values[i] is not null) presentCount++;

        if (presentCount < docCount)
        {
            var bitmap = new RoaringBitmap();
            for (int i = 0; i < docCount; i++)
                if (values[i] is not null) bitmap.Add(i);
            using var bitmapMs = new MemoryStream();
            using var bitmapBw = new BinaryWriter(bitmapMs, Encoding.UTF8, leaveOpen: true);
            bitmap.Serialise(bitmapBw);
            bitmapBw.Flush();
            int bitmapLen = (int)bitmapMs.Length;
            bw.WriteInt32(bitmapLen);
            bw.WriteBytes(bitmapMs.GetBuffer(), 0, bitmapLen);
        }
        else
        {
            bw.WriteInt32(0); // all docs present
        }

        bw.WriteInt32(docCount);

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

        bw.WriteInt32(ordList.Count);
        foreach (var ord in ordList)
            bw.WriteString(ord);

        int bitsPerOrd = ordList.Count <= 1 ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)(ordList.Count - 1));
        bw.WriteByte((byte)bitsPerOrd);

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
                    bw.WriteByte((byte)(buffer & 0xFF));
                    buffer >>= 8;
                    bitsInBuffer -= 8;
                }
            }
            if (bitsInBuffer > 0)
                bw.WriteByte((byte)(buffer & 0xFF));
        }
    }

}

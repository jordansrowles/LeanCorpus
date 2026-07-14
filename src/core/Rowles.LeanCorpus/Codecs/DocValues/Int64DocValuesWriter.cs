using System.Buffers;
using System.IO;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>
/// Writes per-document 64-bit integer values in a compact column-stride format (.dvnl).
/// Layout per field: [fieldName] [presenceByteCount: int32] [presenceBitmap: bytes if count > 0]
/// [docCount: int32] [minValue: int64] [bitsPerValue: byte] [packed values...].
/// A zero presence byte count means all documents are present.
/// </summary>
internal static class Int64DocValuesWriter
{
    public static void Write(
        string filePath,
        IReadOnlyDictionary<string, long[]> fields,
        int docCount,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? presenceSets = null,
        bool durable = false)
    {
        var bodyBuf = new ArrayBufferWriter<byte>(4096);
        bodyBuf.WriteInt32(fields.Count);

        foreach (var (fieldName, values) in fields)
        {
            IReadOnlySet<int>? presenceSet = null;
            presenceSets?.TryGetValue(fieldName, out presenceSet);
            WriteFieldBlock(bodyBuf, fieldName, values, docCount, presenceSet);
        }

        using var output = new IndexOutput(filePath, durable);
        CodecFileHeader.Write(output, CodecFormats.Int64DocValues, bodyBuf.WrittenSpan);
    }

    internal static void WriteFieldBlock(
        IBufferWriter<byte> bw,
        string fieldName,
        long[] values,
        int docCount,
        IReadOnlySet<int>? presenceSet = null)
    {
        bw.WriteString(fieldName);

        if (presenceSet is not null && presenceSet.Count < docCount)
        {
            var bitmap = new RoaringBitmap();
            foreach (int docId in presenceSet)
                bitmap.Add(docId);
            using var ms = new MemoryStream();
            using var bwt = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
            bitmap.Serialise(bwt);
            bwt.Flush();
            int bitmapLen = (int)ms.Length;
            bw.WriteInt32(bitmapLen);
            bw.WriteBytes(ms.GetBuffer(), 0, bitmapLen);
        }
        else
        {
            bw.WriteInt32(0);
        }

        bw.WriteInt32(docCount);

        long min = long.MaxValue, max = long.MinValue;
        for (int i = 0; i < docCount; i++)
        {
            long v = values[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }

        bw.WriteInt64(min);
        ulong range = (ulong)(max - min);
        int bitsPerValue = range == 0 ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount(range);
        bw.WriteByte((byte)bitsPerValue);

        if (bitsPerValue == 0) return;

        byte accum = 0;
        int accBits = 0;
        for (int i = 0; i < docCount; i++)
        {
            ulong delta = (ulong)(values[i] - min);
            int remaining = bitsPerValue;
            while (remaining > 0)
            {
                int space = 8 - accBits;
                int take = Math.Min(remaining, space);
                accum |= (byte)((delta & ((1UL << take) - 1)) << accBits);
                delta >>= take;
                accBits += take;
                remaining -= take;
                if (accBits == 8)
                {
                    bw.WriteByte(accum);
                    accum = 0;
                    accBits = 0;
                }
            }
        }
        if (accBits > 0)
            bw.WriteByte(accum);
    }
}

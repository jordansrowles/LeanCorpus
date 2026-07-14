using System.Buffers;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>
/// Writes multi-valued 64-bit integer DocValues in a column-stride format (.dsnl).
/// </summary>
internal static class Int64SortedNumericDocValuesWriter
{
    public static void Write(
        string filePath,
        IReadOnlyDictionary<string, IReadOnlyList<long>?[]> fields,
        int docCount,
        bool durable = false)
    {
        var bodyBuf = new ArrayBufferWriter<byte>(4096);
        bodyBuf.WriteInt32(fields.Count);

        foreach (var (fieldName, values) in fields)
            WriteFieldBlock(bodyBuf, fieldName, values, docCount);

        using var output = new IndexOutput(filePath, durable);
        CodecFileHeader.Write(output, CodecFormats.Int64SortedNumericDocValues, bodyBuf.WrittenSpan);
    }

    internal static void WriteFieldBlock(
        IBufferWriter<byte> bw,
        string fieldName,
        IReadOnlyList<long>?[] values,
        int docCount)
    {
        bw.WriteString(fieldName);
        bw.WriteInt32(docCount);

        var starts = new int[docCount + 1];
        var flattened = new List<long>();
        for (int docId = 0; docId < docCount; docId++)
        {
            starts[docId] = flattened.Count;
            if ((uint)docId >= (uint)values.Length || values[docId] is not { Count: > 0 } source)
                continue;

            if (IsSorted(source))
            {
                flattened.AddRange(source);
                continue;
            }

            var copy = ArrayPool<long>.Shared.Rent(source.Count);
            try
            {
                for (int i = 0; i < source.Count; i++)
                    copy[i] = source[i];
                Array.Sort(copy, 0, source.Count);
                for (int i = 0; i < source.Count; i++)
                    flattened.Add(copy[i]);
            }
            finally
            {
                ArrayPool<long>.Shared.Return(copy);
            }
        }
        starts[docCount] = flattened.Count;

        for (int i = 0; i < starts.Length; i++)
            bw.WriteInt32(starts[i]);

        bw.WriteInt32(flattened.Count);
        WritePackedInt64s(bw, flattened);
    }

    private static bool IsSorted(IReadOnlyList<long> values)
    {
        for (int i = 1; i < values.Count; i++)
        {
            if (values[i - 1].CompareTo(values[i]) > 0)
                return false;
        }

        return true;
    }

    private static void WritePackedInt64s(IBufferWriter<byte> bw, IReadOnlyList<long> values)
    {
        if (values.Count == 0)
        {
            bw.WriteInt64(0L);
            bw.WriteByte((byte)0);
            return;
        }

        long min = long.MaxValue;
        long max = long.MinValue;
        for (int i = 0; i < values.Count; i++)
        {
            long v = values[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }

        bw.WriteInt64(min);
        ulong range = (ulong)(max - min);
        int bitsPerValue = range == 0 ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount(range);
        bw.WriteByte((byte)bitsPerValue);

        if (bitsPerValue == 0)
            return;

        byte accum = 0;
        int accBits = 0;
        for (int i = 0; i < values.Count; i++)
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

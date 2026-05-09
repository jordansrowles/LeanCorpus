using System.Buffers;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Codecs.DocValues;

/// <summary>
/// Writes multi-valued numeric DocValues in a column-stride format (.dsn).
/// </summary>
internal static class SortedNumericDocValuesWriter
{
    public static void Write(
        string filePath,
        IReadOnlyDictionary<string, IReadOnlyList<double>?[]> fields,
        int docCount,
        bool durable = false)
    {
        using var output = new IndexOutput(filePath, durable);
        CodecConstants.WriteHeader(output, CodecConstants.SortedNumericDocValuesVersion);
        output.WriteInt32(fields.Count);

        foreach (var (fieldName, values) in fields)
            WriteFieldBlock(output, fieldName, values, docCount);
    }

    internal static void WriteFieldBlock(
        IndexOutput output,
        string fieldName,
        IReadOnlyList<double>?[] values,
        int docCount)
    {
        WriteString(output, fieldName);
        output.WriteInt32(docCount);

        var starts = new int[docCount + 1];
        var flattened = new List<double>();
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

            var copy = ArrayPool<double>.Shared.Rent(source.Count);
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
                ArrayPool<double>.Shared.Return(copy);
            }
        }
        starts[docCount] = flattened.Count;

        for (int i = 0; i < starts.Length; i++)
            output.WriteInt32(starts[i]);

        output.WriteInt32(flattened.Count);
        WritePackedDoubles(output, flattened);
    }

    private static bool IsSorted(IReadOnlyList<double> values)
    {
        for (int i = 1; i < values.Count; i++)
        {
            if (values[i - 1].CompareTo(values[i]) > 0)
                return false;
        }

        return true;
    }

    private static void WritePackedDoubles(IndexOutput output, IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            output.WriteInt64(0);
            output.WriteByte(0);
            return;
        }

        long min = long.MaxValue;
        long max = long.MinValue;
        for (int i = 0; i < values.Count; i++)
        {
            long bits = BitConverter.DoubleToInt64Bits(values[i]);
            if (bits < min) min = bits;
            if (bits > max) max = bits;
        }

        output.WriteInt64(min);
        ulong range = (ulong)max - (ulong)min;
        int bitsPerValue = range == 0 ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount(range);
        output.WriteByte((byte)bitsPerValue);

        if (bitsPerValue == 0)
            return;

        byte accum = 0;
        int accBits = 0;
        for (int i = 0; i < values.Count; i++)
        {
            ulong delta = (ulong)BitConverter.DoubleToInt64Bits(values[i]) - (ulong)min;
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
                    output.WriteByte(accum);
                    accum = 0;
                    accBits = 0;
                }
            }
        }

        if (accBits > 0)
            output.WriteByte(accum);
    }

    private static void WriteString(IndexOutput output, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        output.WriteVarInt(bytes.Length);
        output.WriteBytes(bytes);
    }
}

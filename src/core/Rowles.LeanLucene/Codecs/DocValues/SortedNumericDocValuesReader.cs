using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Codecs.DocValues;

/// <summary>
/// Reads multi-valued numeric DocValues from a .dsn sidecar file.
/// </summary>
internal static class SortedNumericDocValuesReader
{
    public static Dictionary<string, double[][]> Read(string filePath)
    {
        var values = new Dictionary<string, double[][]>(StringComparer.Ordinal);
        if (!File.Exists(filePath))
            return values;

        using var input = new IndexInput(filePath);
        CodecConstants.ReadHeaderVersion(input, CodecConstants.SortedNumericDocValuesVersion, "sorted-numeric doc values (.dsn)");

        int fieldCount = input.ReadInt32();
        for (int f = 0; f < fieldCount; f++)
        {
            string fieldName = ReadString(input);
            int docCount = input.ReadInt32();
            var starts = new int[docCount + 1];
            for (int i = 0; i < starts.Length; i++)
                starts[i] = input.ReadInt32();

            int valueCount = input.ReadInt32();
            ValidateStarts(starts, valueCount, fieldName);
            var flattened = ReadPackedDoubles(input, valueCount);

            var perDoc = new double[docCount][];
            for (int docId = 0; docId < docCount; docId++)
            {
                int start = starts[docId];
                int end = starts[docId + 1];
                if (end == start)
                {
                    perDoc[docId] = [];
                    continue;
                }

                var docValues = new double[end - start];
                Array.Copy(flattened, start, docValues, 0, docValues.Length);
                perDoc[docId] = docValues;
            }

            values[fieldName] = perDoc;
        }

        return values;
    }

    private static double[] ReadPackedDoubles(IndexInput input, int valueCount)
    {
        long min = input.ReadInt64();
        int bitsPerValue = input.ReadByte();
        var values = new double[valueCount];

        if (valueCount == 0)
            return values;

        if (bitsPerValue == 0)
        {
            double value = BitConverter.Int64BitsToDouble(min);
            Array.Fill(values, value);
            return values;
        }

        byte accum = 0;
        int accBits = 0;
        for (int i = 0; i < values.Length; i++)
        {
            ulong value = 0;
            int collected = 0;
            while (collected < bitsPerValue)
            {
                if (accBits == 0)
                {
                    accum = input.ReadByte();
                    accBits = 8;
                }

                int take = Math.Min(bitsPerValue - collected, accBits);
                value |= ((ulong)(accum & ((1 << take) - 1))) << collected;
                accum >>= take;
                accBits -= take;
                collected += take;
            }

            values[i] = BitConverter.Int64BitsToDouble((long)((ulong)min + value));
        }

        return values;
    }

    private static void ValidateStarts(int[] starts, int totalValues, string fieldName)
    {
        if (starts[0] != 0)
            throw new InvalidDataException($"Invalid sorted-numeric DocValues offsets for field '{fieldName}'.");

        int previous = 0;
        for (int i = 0; i < starts.Length; i++)
        {
            int current = starts[i];
            if (current < previous || current > totalValues)
                throw new InvalidDataException($"Invalid sorted-numeric DocValues offsets for field '{fieldName}'.");
            previous = current;
        }

        if (starts[^1] != totalValues)
            throw new InvalidDataException($"Invalid sorted-numeric DocValues terminal offset for field '{fieldName}'.");
    }

    private static string ReadString(IndexInput input)
    {
        int length = input.ReadVarInt();
        if (length < 0)
            throw new InvalidDataException("Negative string length in sorted-numeric DocValues.");
        return System.Text.Encoding.UTF8.GetString(input.ReadBytes(length));
    }
}

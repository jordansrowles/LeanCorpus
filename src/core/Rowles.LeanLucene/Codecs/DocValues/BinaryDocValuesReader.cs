using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Codecs.DocValues;

/// <summary>
/// Reads multi-valued binary DocValues from a .dvb sidecar file.
/// </summary>
internal static class BinaryDocValuesReader
{
    public static Dictionary<string, byte[][][]> Read(string filePath)
    {
        var values = new Dictionary<string, byte[][][]>(StringComparer.Ordinal);
        if (!File.Exists(filePath))
            return values;

        using var input = new IndexInput(filePath);
        CodecConstants.ReadHeaderVersion(input, CodecConstants.BinaryDocValuesVersion, "binary doc values (.dvb)");

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

            var byteOffsets = new int[valueCount + 1];
            for (int i = 0; i < byteOffsets.Length; i++)
                byteOffsets[i] = input.ReadInt32();
            ValidateStarts(byteOffsets, byteOffsets[^1], fieldName);

            var payload = input.ReadBytes(byteOffsets[^1]);
            var allValues = new byte[valueCount][];
            for (int i = 0; i < allValues.Length; i++)
            {
                int start = byteOffsets[i];
                int length = byteOffsets[i + 1] - start;
                var value = new byte[length];
                Array.Copy(payload, start, value, 0, length);
                allValues[i] = value;
            }

            var perDoc = new byte[docCount][][];
            for (int docId = 0; docId < docCount; docId++)
            {
                int start = starts[docId];
                int end = starts[docId + 1];
                if (end == start)
                {
                    perDoc[docId] = [];
                    continue;
                }

                var docValues = new byte[end - start][];
                Array.Copy(allValues, start, docValues, 0, docValues.Length);
                perDoc[docId] = docValues;
            }

            values[fieldName] = perDoc;
        }

        return values;
    }

    private static void ValidateStarts(int[] starts, int totalValues, string fieldName)
    {
        if (starts[0] != 0)
            throw new InvalidDataException($"Invalid binary DocValues offsets for field '{fieldName}'.");

        int previous = 0;
        for (int i = 0; i < starts.Length; i++)
        {
            int current = starts[i];
            if (current < previous || current > totalValues)
                throw new InvalidDataException($"Invalid binary DocValues offsets for field '{fieldName}'.");
            previous = current;
        }

        if (starts[^1] != totalValues)
            throw new InvalidDataException($"Invalid binary DocValues terminal offset for field '{fieldName}'.");
    }

    private static string ReadString(IndexInput input)
    {
        int length = input.ReadVarInt();
        if (length < 0)
            throw new InvalidDataException("Negative string length in binary DocValues.");
        return System.Text.Encoding.UTF8.GetString(input.ReadBytes(length));
    }
}

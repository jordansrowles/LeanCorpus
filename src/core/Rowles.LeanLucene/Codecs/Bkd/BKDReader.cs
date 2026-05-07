namespace Rowles.LeanLucene.Codecs.Bkd;

/// <summary>
/// Reads a 1-dimensional BKD tree for efficient numeric range lookups.
/// Uses memory-mapped IndexInput for zero-copy seeks.
/// </summary>
internal sealed class BKDReader : IDisposable
{
    private readonly Store.IndexInput _input;
    private readonly Dictionary<string, long> _fieldOffsets;

    private BKDReader(Store.IndexInput input, Dictionary<string, long> fieldOffsets)
    {
        _input = input;
        _fieldOffsets = fieldOffsets;
    }

    public static BKDReader Open(string filePath)
    {
        var input = new Store.IndexInput(filePath);

        CodecConstants.ValidateHeader(input, CodecConstants.BKDVersion, "BKD tree (.bkd)");

        int fieldCount = input.ReadInt32();
        var offsets = new Dictionary<string, long>(fieldCount, StringComparer.Ordinal);
        for (int f = 0; f < fieldCount; f++)
        {
            string fieldName = input.ReadLengthPrefixedString();
            offsets[fieldName] = input.Position;
            SkipNode(input);
        }

        return new BKDReader(input, offsets);
    }

    /// <summary>Returns all (docId, value) pairs in [min, max] range for the given field.</summary>
    public List<(int DocId, double Value)> RangeQuery(string field, double min, double max)
    {
        var results = new List<(int, double)>();
        if (!_fieldOffsets.TryGetValue(field, out long offset))
            return results;

        _input.Seek(offset);
        SearchNode(_input, min, max, results);
        return results;
    }

    public bool HasField(string field) => _fieldOffsets.ContainsKey(field);

    private static void SearchNode(Store.IndexInput input, double min, double max, List<(int, double)> results)
    {
        byte marker = input.ReadByte();
        if (marker == 1) // leaf
        {
            int count = input.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                double value = input.ReadDouble();
                int docId = input.ReadInt32();
                if (value >= min && value <= max)
                    results.Add((docId, value));
            }
        }
        else // internal
        {
            double splitValue = input.ReadDouble();
            if (min <= splitValue)
                SearchNode(input, min, max, results);
            else
                SkipNode(input);

            if (max >= splitValue)
                SearchNode(input, min, max, results);
            else
                SkipNode(input);
        }
    }

    private static void SkipNode(Store.IndexInput input)
    {
        byte marker = input.ReadByte();
        if (marker == 1) // leaf
        {
            int count = input.ReadInt32();
            input.Seek(input.Position + count * 12L);
        }
        else // internal
        {
            input.ReadDouble(); // split value
            SkipNode(input);
            SkipNode(input);
        }
    }

    public void Dispose() => _input.Dispose();
}

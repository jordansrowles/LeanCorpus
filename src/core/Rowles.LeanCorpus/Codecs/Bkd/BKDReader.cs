using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;

namespace Rowles.LeanCorpus.Codecs.Bkd;

/// <summary>
/// Reads a 1-dimensional BKD tree for efficient numeric range lookups.
/// Uses memory-mapped IndexInput for zero-copy seeks.
/// </summary>
internal sealed class BKDReader : IDisposable
{
    private const int MaxBkdDepth = 64;
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

        CodecFileHeader.ReadVersion(input, CodecFormats.Bkd);

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

    /// <summary>Visits all (docId, value) pairs in [min, max] range for the given field.</summary>
    internal bool VisitRange(string field, double min, double max, Action<int, double> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        if (!_fieldOffsets.TryGetValue(field, out long offset))
            return false;

        _input.Seek(offset);
        SearchNode(_input, min, max, visitor);
        return true;
    }

    /// <summary>Returns all (docId, value) pairs in [min, max] range for the given field.</summary>
    public List<(int DocId, double Value)> RangeQuery(string field, double min, double max)
    {
        var results = new List<(int, double)>();
        VisitRange(field, min, max, (docId, value) => results.Add((docId, value)));
        return results;
    }

    /// <summary>Returns all (docId, value) pairs whose value is contained in the supplied set.</summary>
    public List<(int DocId, double Value)> ExactSetQuery(string field, IReadOnlySet<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var results = new List<(int, double)>();
        if (values.Count == 0 || !_fieldOffsets.TryGetValue(field, out long offset))
            return results;

        _input.Seek(offset);
        SearchNodeExactSet(_input, values, results);
        return results;
    }

    public bool HasField(string field) => _fieldOffsets.ContainsKey(field);

    private static void SearchNode(Store.IndexInput input, double min, double max, Action<int, double> visitor, int depth = 0)
    {
        if (depth > MaxBkdDepth)
            throw new InvalidDataException("BKD tree exceeds maximum recursion depth.");

        byte marker = input.ReadByte();
        if (marker == 1)
        {
            int count = ValidateLeafCount(input);
            for (int i = 0; i < count; i++)
            {
                double value = input.ReadDouble();
                int docId = input.ReadInt32();
                if (value >= min && value <= max)
                    visitor(docId, value);
            }
        }
        else if (marker == 0)
        {
            double splitValue = input.ReadDouble();
            if (min <= splitValue)
                SearchNode(input, min, max, visitor, depth + 1);
            else
                SkipNode(input, depth + 1);

            if (max >= splitValue)
                SearchNode(input, min, max, visitor, depth + 1);
            else
                SkipNode(input, depth + 1);
        }
        else
        {
            throw new InvalidDataException($"BKD tree has invalid node marker: {marker}.");
        }
    }

    private static void SearchNodeExactSet(Store.IndexInput input, IReadOnlySet<double> values, List<(int DocId, double Value)> results, int depth = 0)
    {
        if (depth > MaxBkdDepth)
            throw new InvalidDataException("BKD tree exceeds maximum recursion depth.");

        byte marker = input.ReadByte();
        if (marker == 1)
        {
            int count = ValidateLeafCount(input);
            for (int i = 0; i < count; i++)
            {
                double value = input.ReadDouble();
                int docId = input.ReadInt32();
                if (values.Contains(value))
                    results.Add((docId, value));
            }
        }
        else if (marker == 0)
        {
            input.ReadDouble(); // split value
            SearchNodeExactSet(input, values, results, depth + 1);
            SearchNodeExactSet(input, values, results, depth + 1);
        }
        else
        {
            throw new InvalidDataException($"BKD tree has invalid node marker: {marker}.");
        }
    }

    private static void SkipNode(Store.IndexInput input, int depth = 0)
    {
        if (depth > MaxBkdDepth)
            throw new InvalidDataException("BKD tree exceeds maximum recursion depth.");

        byte marker = input.ReadByte();
        if (marker == 1)
        {
            int count = ValidateLeafCount(input);
            input.Seek(input.Position + count * 12L);
        }
        else if (marker == 0)
        {
            input.ReadDouble(); // split value
            SkipNode(input, depth + 1);
            SkipNode(input, depth + 1);
        }
        else
        {
            throw new InvalidDataException($"BKD tree has invalid node marker: {marker}.");
        }
    }

    private static int ValidateLeafCount(Store.IndexInput input)
    {
        int count = input.ReadInt32();
        if (count < 0)
            throw new InvalidDataException($"BKD tree has negative leaf count: {count}.");
        long remaining = input.Length - input.Position;
        if (count > remaining / 12)
            throw new InvalidDataException($"BKD tree leaf count {count} exceeds remaining bytes ({remaining}).");
        return count;
    }

    public void Dispose() => _input.Dispose();
}

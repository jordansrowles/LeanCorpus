using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;

namespace Rowles.LeanCorpus.Codecs.Bkd;

/// <summary>
/// Reads a 1-dimensional BKD tree for efficient 64-bit integer range lookups.
/// Uses memory-mapped IndexInput for zero-copy seeks.
/// </summary>
internal sealed class Int64BKDReader : IDisposable
{
    private readonly Store.IndexInput _input;
    private readonly Dictionary<string, long> _fieldOffsets;

    private Int64BKDReader(Store.IndexInput input, Dictionary<string, long> fieldOffsets)
    {
        _input = input;
        _fieldOffsets = fieldOffsets;
    }

    public static Int64BKDReader Open(string filePath)
    {
        var input = new Store.IndexInput(filePath);

        CodecFileHeader.ReadVersion(input, CodecFormats.Int64Bkd);

        int fieldCount = input.ReadInt32();
        var offsets = new Dictionary<string, long>(fieldCount, StringComparer.Ordinal);
        for (int f = 0; f < fieldCount; f++)
        {
            string fieldName = input.ReadLengthPrefixedString();
            offsets[fieldName] = input.Position;
            SkipNode(input);
        }

        return new Int64BKDReader(input, offsets);
    }

    /// <summary>Visits all (docId, value) pairs in [min, max] range for the given field.</summary>
    internal bool VisitRange(string field, long min, long max, Action<int, long> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        if (!_fieldOffsets.TryGetValue(field, out long offset))
            return false;

        _input.Seek(offset);
        SearchNode(_input, min, max, visitor);
        return true;
    }

    /// <summary>Returns all (docId, value) pairs in [min, max] range for the given field.</summary>
    public List<(int DocId, long Value)> RangeQuery(string field, long min, long max)
    {
        var results = new List<(int, long)>();
        VisitRange(field, min, max, (docId, value) => results.Add((docId, value)));
        return results;
    }

    /// <summary>Returns all (docId, value) pairs whose value is contained in the supplied set.</summary>
    public List<(int DocId, long Value)> ExactSetQuery(string field, IReadOnlySet<long> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var results = new List<(int DocId, long Value)>();
        if (values.Count == 0 || !_fieldOffsets.TryGetValue(field, out long offset))
            return results;

        _input.Seek(offset);
        SearchNodeExactSet(_input, values, results);
        return results;
    }

    public bool HasField(string field) => _fieldOffsets.ContainsKey(field);

    private static void SearchNode(Store.IndexInput input, long min, long max, Action<int, long> visitor)
    {
        byte marker = input.ReadByte();
        if (marker == 1) // leaf
        {
            int count = input.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                long value = input.ReadInt64();
                int docId = input.ReadInt32();
                if (value >= min && value <= max)
                    visitor(docId, value);
            }
        }
        else // internal
        {
            long splitValue = input.ReadInt64();
            if (min <= splitValue)
                SearchNode(input, min, max, visitor);
            else
                SkipNode(input);

            if (max >= splitValue)
                SearchNode(input, min, max, visitor);
            else
                SkipNode(input);
        }
    }

    private static void SearchNodeExactSet(Store.IndexInput input, IReadOnlySet<long> values, List<(int DocId, long Value)> results)
    {
        byte marker = input.ReadByte();
        if (marker == 1)
        {
            int count = input.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                long value = input.ReadInt64();
                int docId = input.ReadInt32();
                if (values.Contains(value))
                    results.Add((docId, value));
            }
        }
        else
        {
            input.ReadInt64(); // split value
            SearchNodeExactSet(input, values, results);
            SearchNodeExactSet(input, values, results);
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
            input.ReadInt64(); // split value
            SkipNode(input);
            SkipNode(input);
        }
    }

    public void Dispose() => _input.Dispose();
}

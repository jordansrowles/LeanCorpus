using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Codecs.Bkd;

/// <summary>
/// Reads a 1-dimensional BKD tree for efficient 64-bit integer range lookups.
/// Uses memory-mapped IndexInput for zero-copy seeks.
/// </summary>
internal sealed class Int64BKDReader : IDisposable
{
    private const int MaxBkdDepth = 64;
    private readonly Store.IndexInput _input;
    private readonly Dictionary<string, long> _fieldOffsets;
    private readonly long _bodyEnd;
    private readonly BkdReadFrame _frame;

    private Int64BKDReader(Store.IndexInput input, Dictionary<string, long> fieldOffsets, long bodyEnd, BkdReadFrame frame)
    {
        _input = input;
        _fieldOffsets = fieldOffsets;
        _bodyEnd = bodyEnd;
        _frame = frame;
    }

    public static Int64BKDReader Open(string filePath)
    {
        return Open(new Store.IndexInput(filePath));
    }

    internal static Int64BKDReader Open(Store.IndexInput input)
    {
        BkdReadFrame? frame = null;
        try
        {
            frame = BkdCodecFiles.Open(input, BkdCodecFiles.Int64);

            int fieldCount = input.ReadInt32();
            if (fieldCount < 0 || fieldCount > (frame.BodyEnd - input.Position) / 6)
                throw new InvalidDataException($"Int64 BKD field count {fieldCount} is invalid for the remaining body.");
            var offsets = new Dictionary<string, long>(fieldCount, StringComparer.Ordinal);
            for (int f = 0; f < fieldCount; f++)
            {
                string fieldName = input.ReadLengthPrefixedString();
                offsets[fieldName] = input.Position;
                SkipNode(input, frame.BodyEnd);
            }

            if (input.Position != frame.BodyEnd)
                throw new InvalidDataException("Int64 BKD body contains trailing or unparsed bytes.");
            var result = new Int64BKDReader(input, offsets, frame.BodyEnd, frame);
            frame = null;
            return result;
        }
        catch
        {
            frame?.Dispose();
            input.Dispose();
            throw;
        }
    }

    /// <summary>Visits all (docId, value) pairs in [min, max] range for the given field.</summary>
    internal bool VisitRange(string field, long min, long max, Action<int, long> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        if (!_fieldOffsets.TryGetValue(field, out long offset))
            return false;

        _input.Seek(offset);
        SearchNode(_input, _bodyEnd, min, max, visitor);
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
        SearchNodeExactSet(_input, _bodyEnd, values, results);
        return results;
    }

    public bool HasField(string field) => _fieldOffsets.ContainsKey(field);

    private static void SearchNode(Store.IndexInput input, long bodyEnd, long min, long max, Action<int, long> visitor, int depth = 0)
    {
        if (depth > MaxBkdDepth)
            throw new InvalidDataException("Int64 BKD tree exceeds maximum recursion depth.");
        byte marker = input.ReadByte();
        if (marker == 1) // leaf
        {
            int count = ValidateLeafCount(input, bodyEnd);
            for (int i = 0; i < count; i++)
            {
                long value = input.ReadInt64();
                int docId = input.ReadInt32();
                if (value >= min && value <= max)
                    visitor(docId, value);
            }
        }
        else if (marker == 0)
        {
            long splitValue = input.ReadInt64();
            if (min <= splitValue)
                SearchNode(input, bodyEnd, min, max, visitor, depth + 1);
            else
                SkipNode(input, bodyEnd, depth + 1);

            if (max >= splitValue)
                SearchNode(input, bodyEnd, min, max, visitor, depth + 1);
            else
                SkipNode(input, bodyEnd, depth + 1);
        }
        else
            throw new InvalidDataException($"Int64 BKD tree has invalid node marker: {marker}.");
    }

    private static void SearchNodeExactSet(Store.IndexInput input, long bodyEnd, IReadOnlySet<long> values, List<(int DocId, long Value)> results, int depth = 0)
    {
        if (depth > MaxBkdDepth)
            throw new InvalidDataException("Int64 BKD tree exceeds maximum recursion depth.");
        byte marker = input.ReadByte();
        if (marker == 1)
        {
            int count = ValidateLeafCount(input, bodyEnd);
            for (int i = 0; i < count; i++)
            {
                long value = input.ReadInt64();
                int docId = input.ReadInt32();
                if (values.Contains(value))
                    results.Add((docId, value));
            }
        }
        else if (marker == 0)
        {
            input.ReadInt64(); // split value
            SearchNodeExactSet(input, bodyEnd, values, results, depth + 1);
            SearchNodeExactSet(input, bodyEnd, values, results, depth + 1);
        }
        else
            throw new InvalidDataException($"Int64 BKD tree has invalid node marker: {marker}.");
    }

    private static void SkipNode(Store.IndexInput input, long bodyEnd, int depth = 0)
    {
        if (depth > MaxBkdDepth)
            throw new InvalidDataException("Int64 BKD tree exceeds maximum recursion depth.");
        byte marker = input.ReadByte();
        if (marker == 1) // leaf
        {
            int count = ValidateLeafCount(input, bodyEnd);
            input.Seek(input.Position + count * 12L);
        }
        else if (marker == 0)
        {
            input.ReadInt64(); // split value
            SkipNode(input, bodyEnd, depth + 1);
            SkipNode(input, bodyEnd, depth + 1);
        }
        else
            throw new InvalidDataException($"Int64 BKD tree has invalid node marker: {marker}.");
    }

    private static int ValidateLeafCount(Store.IndexInput input, long bodyEnd)
    {
        int count = input.ReadInt32();
        long remaining = bodyEnd - input.Position;
        if (count < 0 || count > remaining / 12)
            throw new InvalidDataException($"Int64 BKD leaf count {count} is invalid for the remaining body.");
        return count;
    }

    internal IReadOnlyCollection<string> FieldNames => _fieldOffsets.Keys;

    public void Dispose()
    {
        _frame.Dispose();
        _input.Dispose();
    }
}

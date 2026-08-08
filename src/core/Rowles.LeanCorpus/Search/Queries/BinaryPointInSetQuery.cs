namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches binary doc values equal to any supplied byte sequence.</summary>
public sealed class BinaryPointInSetQuery : Query
{
    private readonly byte[][] _points;

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the distinct, lexicographically sorted point set.</summary>
    public IReadOnlyList<byte[]> Points => _points;

    /// <summary>Initialises a binary point-in-set query.</summary>
    public BinaryPointInSetQuery(string field, params byte[][] points)
        : this(field, (IEnumerable<byte[]>)points)
    {
    }

    /// <summary>Initialises a binary point-in-set query.</summary>
    public BinaryPointInSetQuery(string field, IEnumerable<byte[]> points)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field must be a non-empty value.", nameof(field));
        ArgumentNullException.ThrowIfNull(points);

        Field = field;
        var normalised = new SortedSet<byte[]>(ByteArrayComparer.Instance);
        foreach (var point in points)
        {
            ArgumentNullException.ThrowIfNull(point);
            normalised.Add(point.ToArray());
        }

        _points = normalised.ToArray();
        if (_points.Length == 0)
            throw new ArgumentException("BinaryPointInSetQuery requires at least one point.", nameof(points));
    }

    internal bool Contains(ReadOnlySpan<byte> value)
    {
        int low = 0;
        int high = _points.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = value.SequenceCompareTo(_points[middle]);
            if (comparison == 0)
                return true;
            if (comparison < 0)
                high = middle - 1;
            else
                low = middle + 1;
        }
        return false;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is not BinaryPointInSetQuery other ||
            !string.Equals(Field, other.Field, StringComparison.Ordinal) ||
            Boost != other.Boost ||
            _points.Length != other._points.Length)
        {
            return false;
        }

        for (int i = 0; i < _points.Length; i++)
        {
            if (!_points[i].AsSpan().SequenceEqual(other._points[i]))
                return false;
        }
        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(BinaryPointInSetQuery));
        hash.Add(Field);
        foreach (var point in _points)
            hash.AddBytes(point);
        return CombineBoost(hash.ToHashCode());
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        internal static readonly ByteArrayComparer Instance = new();

        public int Compare(byte[]? left, byte[]? right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left is null)
                return -1;
            if (right is null)
                return 1;
            return left.AsSpan().SequenceCompareTo(right);
        }
    }
}

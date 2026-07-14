namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches documents whose 64-bit integer point value is equal to any value in the supplied set.</summary>
public sealed class Int64PointInSetQuery : Query
{
    private readonly long[] _points;

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the distinct, sorted point set.</summary>
    public IReadOnlyList<long> Points => _points;

    /// <summary>Initialises a new <see cref="Int64PointInSetQuery"/>.</summary>
    public Int64PointInSetQuery(string field, params long[] points)
        : this(field, (IEnumerable<long>)points)
    {
    }

    /// <summary>Initialises a new <see cref="Int64PointInSetQuery"/>.</summary>
    public Int64PointInSetQuery(string field, IEnumerable<long> points)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field must be a non-empty value.", nameof(field));
        ArgumentNullException.ThrowIfNull(points);
        Field = field;

        var normalisedPoints = new SortedSet<long>();
        foreach (var point in points)
            normalisedPoints.Add(point);

        _points = normalisedPoints.ToArray();
        if (_points.Length == 0)
            throw new ArgumentException("Int64PointInSetQuery requires at least one point.", nameof(points));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is Int64PointInSetQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        Math.Abs(Boost - other.Boost) < 1e-6f &&
        _points.AsSpan().SequenceEqual(other._points);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(Int64PointInSetQuery));
        hash.Add(Field);
        foreach (var point in _points)
            hash.Add(point);
        return CombineBoost(hash.ToHashCode());
    }
}

namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches documents whose numeric point value is equal to any value in the supplied set.</summary>
public sealed class PointInSetQuery : Query
{
    private readonly double[] _points;

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the distinct, sorted point set.</summary>
    public IReadOnlyList<double> Points => _points;

    /// <summary>Initialises a new <see cref="PointInSetQuery"/>.</summary>
    public PointInSetQuery(string field, params double[] points)
        : this(field, (IEnumerable<double>)points)
    {
    }

    /// <summary>Initialises a new <see cref="PointInSetQuery"/>.</summary>
    public PointInSetQuery(string field, IEnumerable<double> points)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field must be a non-empty value.", nameof(field));
        ArgumentNullException.ThrowIfNull(points);
        Field = field;

        var normalisedPoints = new SortedSet<double>();
        foreach (var point in points)
        {
            if (!double.IsFinite(point))
                throw new ArgumentOutOfRangeException(nameof(points), "Point values must be finite.");
            normalisedPoints.Add(point);
        }

        _points = normalisedPoints.ToArray();
        if (_points.Length == 0)
            throw new ArgumentException("PointInSetQuery requires at least one point.", nameof(points));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is PointInSetQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        Boost == other.Boost &&
        _points.AsSpan().SequenceEqual(other._points);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(PointInSetQuery));
        hash.Add(Field);
        foreach (var point in _points)
            hash.Add(point);
        return CombineBoost(hash.ToHashCode());
    }
}

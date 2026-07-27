namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches any supplied 32-bit integer point.</summary>
public sealed class Int32PointInSetQuery : Query
{
    private readonly int[] _points;

    /// <inheritdoc/>
    public override string Field { get; }
    /// <summary>Gets the sorted, distinct points.</summary>
    public IReadOnlyList<int> Points => _points;

    /// <summary>Initialises a 32-bit integer point-in-set query.</summary>
    public Int32PointInSetQuery(string field, params int[] points)
    {
        Field = field;
        _points = points.Distinct().Order().ToArray();
        if (_points.Length == 0)
            throw new ArgumentException("At least one point is required.", nameof(points));
    }

    /// <inheritdoc/>
    public override Query Rewrite() => new Int64PointInSetQuery(
        Field, _points.Select(static point => (long)point))
    {
        Boost = Boost
    };

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is Int32PointInSetQuery other &&
        Field == other.Field && Boost == other.Boost &&
        _points.AsSpan().SequenceEqual(other._points);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(Int32PointInSetQuery));
        hash.Add(Field);
        foreach (var point in _points)
            hash.Add(point);
        return CombineBoost(hash.ToHashCode());
    }
}

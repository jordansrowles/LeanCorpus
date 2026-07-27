namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches any supplied single-precision point.</summary>
public sealed class SinglePointInSetQuery : Query
{
    private readonly float[] _points;

    /// <inheritdoc/>
    public override string Field { get; }
    /// <summary>Gets the sorted, distinct points.</summary>
    public IReadOnlyList<float> Points => _points;

    /// <summary>Initialises a single-precision point-in-set query.</summary>
    public SinglePointInSetQuery(string field, params float[] points)
    {
        Field = field;
        _points = points.Distinct().Order().ToArray();
        if (_points.Length == 0)
            throw new ArgumentException("At least one point is required.", nameof(points));
    }

    /// <inheritdoc/>
    public override Query Rewrite() => new PointInSetQuery(
        Field, _points.Select(static point => (double)point))
    {
        Boost = Boost
    };

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is SinglePointInSetQuery other &&
        Field == other.Field && Boost == other.Boost &&
        _points.AsSpan().SequenceEqual(other._points);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(SinglePointInSetQuery));
        hash.Add(Field);
        foreach (var point in _points)
            hash.Add(point);
        return CombineBoost(hash.ToHashCode());
    }
}

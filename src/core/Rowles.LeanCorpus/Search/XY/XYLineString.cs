using System.Collections.ObjectModel;

namespace Rowles.LeanCorpus.Search.XY;

/// <summary>An immutable Cartesian line string.</summary>
public sealed class XYLineString : IXYGeometry, IEquatable<XYLineString>
{
    private readonly XYPoint[] _points;
    private readonly ReadOnlyCollection<XYPoint> _readOnlyPoints;

    /// <summary>Creates a validated Cartesian line string.</summary>
    public XYLineString(IEnumerable<XYPoint> points)
    {
        _points = XYGeometryValidation.CanonicaliseLine(points);
        _readOnlyPoints = Array.AsReadOnly(_points);
    }

    /// <summary>Gets the canonical point sequence as a read-only view.</summary>
    public IReadOnlyList<XYPoint> Points => _readOnlyPoints;

    /// <inheritdoc />
    public bool Equals(XYLineString? other) => other is not null && _points.AsSpan().SequenceEqual(other._points);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XYLineString other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => XYGeometryValidation.SequenceHash(_points);
}

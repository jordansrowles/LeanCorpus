using System.Collections.ObjectModel;
using Rowles.LeanCorpus.Search.Geo.Internal;

namespace Rowles.LeanCorpus.Search.Geo;

/// <summary>An immutable geographic line string with canonical International Date Line seam points.</summary>
public sealed class GeoLineString : IGeoGeometry, IEquatable<GeoLineString>
{
    private readonly GeoPoint[] _points;
    private readonly ReadOnlyCollection<GeoPoint> _readOnlyPoints;

    /// <summary>Creates and canonicalises a geographic line string.</summary>
    public GeoLineString(IEnumerable<GeoPoint> points)
    {
        _points = GeoGeometryValidation.CanonicaliseLine(points);
        _readOnlyPoints = Array.AsReadOnly(_points);
    }

    /// <summary>Gets the canonical point sequence as a read-only view.</summary>
    public IReadOnlyList<GeoPoint> Points => _readOnlyPoints;

    /// <inheritdoc />
    public bool Equals(GeoLineString? other)
        => other is not null && _points.AsSpan().SequenceEqual(other._points);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GeoLineString other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => GeoGeometryValidation.SequenceHash(_points);
}

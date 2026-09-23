using System.Collections.ObjectModel;

namespace Rowles.LeanCorpus.Search.Geo;

/// <summary>An immutable flat collection of non-collection geographic geometries.</summary>
public sealed class GeoGeometryCollection : IGeoGeometry, IEquatable<GeoGeometryCollection>
{
    private readonly IGeoGeometry[] _geometries;
    private readonly ReadOnlyCollection<IGeoGeometry> _readOnlyGeometries;

    /// <summary>Creates a copied, flat geographic geometry collection.</summary>
    public GeoGeometryCollection(IEnumerable<IGeoGeometry> geometries)
    {
        ArgumentNullException.ThrowIfNull(geometries);
        _geometries = geometries.ToArray();
        if (_geometries.Length == 0)
            throw new ArgumentException("A geometry collection must not be empty.", nameof(geometries));
        if (_geometries.Any(static geometry => geometry is null or GeoGeometryCollection))
            throw new ArgumentException("Geometry collections must contain non-null non-collection geometries.", nameof(geometries));
        _readOnlyGeometries = Array.AsReadOnly(_geometries);
    }

    /// <summary>Gets the flat geometry sequence as a read-only view.</summary>
    public IReadOnlyList<IGeoGeometry> Geometries => _readOnlyGeometries;

    /// <inheritdoc />
    public bool Equals(GeoGeometryCollection? other)
        => other is not null && _geometries.AsSpan().SequenceEqual(other._geometries);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GeoGeometryCollection other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => GeoGeometryValidation.SequenceHash(_geometries);
}

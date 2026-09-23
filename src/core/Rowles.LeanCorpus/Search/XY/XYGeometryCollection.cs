using System.Collections.ObjectModel;

namespace Rowles.LeanCorpus.Search.XY;

/// <summary>An immutable flat collection of non-collection Cartesian geometries.</summary>
public sealed class XYGeometryCollection : IXYGeometry, IEquatable<XYGeometryCollection>
{
    private readonly IXYGeometry[] _geometries;
    private readonly ReadOnlyCollection<IXYGeometry> _readOnlyGeometries;

    /// <summary>Creates a copied, flat Cartesian geometry collection.</summary>
    public XYGeometryCollection(IEnumerable<IXYGeometry> geometries)
    {
        ArgumentNullException.ThrowIfNull(geometries);
        _geometries = geometries.ToArray();
        if (_geometries.Length == 0) throw new ArgumentException("A geometry collection must not be empty.", nameof(geometries));
        if (_geometries.Any(static geometry => geometry is null or XYGeometryCollection))
            throw new ArgumentException("Geometry collections must contain non-null non-collection geometries.", nameof(geometries));
        _readOnlyGeometries = Array.AsReadOnly(_geometries);
    }

    /// <summary>Gets the flat geometry sequence as a read-only view.</summary>
    public IReadOnlyList<IXYGeometry> Geometries => _readOnlyGeometries;

    /// <inheritdoc />
    public bool Equals(XYGeometryCollection? other) => other is not null && _geometries.AsSpan().SequenceEqual(other._geometries);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XYGeometryCollection other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => XYGeometryValidation.SequenceHash(_geometries);
}

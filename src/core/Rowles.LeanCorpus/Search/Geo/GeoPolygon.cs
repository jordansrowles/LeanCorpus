using System.Collections.ObjectModel;

namespace Rowles.LeanCorpus.Search.Geo;

/// <summary>An immutable geographic polygon with one shell and zero or more holes.</summary>
public sealed class GeoPolygon : IGeoGeometry, IEquatable<GeoPolygon>
{
    private readonly GeoPoint[] _shell;
    private readonly GeoPoint[][] _holes;
    private readonly ReadOnlyCollection<GeoPoint> _readOnlyShell;
    private readonly ReadOnlyCollection<IReadOnlyList<GeoPoint>> _readOnlyHoles;

    /// <summary>Creates and canonicalises a geographic polygon.</summary>
    public GeoPolygon(
        IEnumerable<GeoPoint> shell,
        IEnumerable<IEnumerable<GeoPoint>>? holes = null)
    {
        GeoGeometryValidation.CanonicalisePolygon(shell, holes, out var shellPoints, out var holePoints);
        _shell = shellPoints;
        _holes = holePoints.ToArray();
        _readOnlyShell = Array.AsReadOnly(_shell);
        var holeViews = new IReadOnlyList<GeoPoint>[_holes.Length];
        for (int i = 0; i < _holes.Length; i++)
            holeViews[i] = Array.AsReadOnly(_holes[i]);
        _readOnlyHoles = Array.AsReadOnly(holeViews);
    }

    /// <summary>Gets the canonical closed shell.</summary>
    public IReadOnlyList<GeoPoint> Shell => _readOnlyShell;

    /// <summary>Gets the canonical closed holes.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPoint>> Holes => _readOnlyHoles;

    /// <inheritdoc />
    public bool Equals(GeoPolygon? other)
    {
        if (other is null || !_shell.AsSpan().SequenceEqual(other._shell) || _holes.Length != other._holes.Length)
            return false;
        for (int i = 0; i < _holes.Length; i++)
            if (!_holes[i].AsSpan().SequenceEqual(other._holes[i]))
                return false;
        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GeoPolygon other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(GeoGeometryValidation.SequenceHash(_shell));
        foreach (var hole in _holes)
            hash.Add(GeoGeometryValidation.SequenceHash(hole));
        return hash.ToHashCode();
    }
}

using System.Collections.ObjectModel;

namespace Rowles.LeanCorpus.Search.XY;

/// <summary>An immutable Cartesian polygon with one shell and zero or more holes.</summary>
public sealed class XYPolygon : IXYGeometry, IEquatable<XYPolygon>
{
    private readonly XYPoint[] _shell;
    private readonly XYPoint[][] _holes;
    private readonly ReadOnlyCollection<XYPoint> _readOnlyShell;
    private readonly ReadOnlyCollection<IReadOnlyList<XYPoint>> _readOnlyHoles;

    /// <summary>Creates a validated Cartesian polygon.</summary>
    public XYPolygon(IEnumerable<XYPoint> shell, IEnumerable<IEnumerable<XYPoint>>? holes = null)
    {
        _shell = XYGeometryValidation.CanonicaliseRing(shell, shellRing: true);
        var holesList = new List<XYPoint[]>();
        if (holes is not null)
            foreach (var hole in holes)
                holesList.Add(XYGeometryValidation.CanonicaliseRing(hole, shellRing: false));
        XYGeometryValidation.ValidateHoles(_shell, holesList);
        _holes = holesList.ToArray();
        _readOnlyShell = Array.AsReadOnly(_shell);
        var holeViews = new IReadOnlyList<XYPoint>[_holes.Length];
        for (int i = 0; i < _holes.Length; i++)
            holeViews[i] = Array.AsReadOnly(_holes[i]);
        _readOnlyHoles = Array.AsReadOnly(holeViews);
    }

    /// <summary>Gets the canonical closed shell.</summary>
    public IReadOnlyList<XYPoint> Shell => _readOnlyShell;

    /// <summary>Gets the canonical closed holes.</summary>
    public IReadOnlyList<IReadOnlyList<XYPoint>> Holes => _readOnlyHoles;

    /// <inheritdoc />
    public bool Equals(XYPolygon? other)
    {
        if (other is null || !_shell.AsSpan().SequenceEqual(other._shell) || _holes.Length != other._holes.Length)
            return false;
        for (int i = 0; i < _holes.Length; i++)
            if (!_holes[i].AsSpan().SequenceEqual(other._holes[i]))
                return false;
        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XYPolygon other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(XYGeometryValidation.SequenceHash(_shell));
        foreach (var hole in _holes)
            hash.Add(XYGeometryValidation.SequenceHash(hole));
        return hash.ToHashCode();
    }
}

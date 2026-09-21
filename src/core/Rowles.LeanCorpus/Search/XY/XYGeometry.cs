using System.Collections.ObjectModel;

namespace Rowles.LeanCorpus.Search.XY;

/// <summary>Marker interface for validated Cartesian geometry.</summary>
public interface XYGeometry
{
}

/// <summary>An immutable Cartesian point.</summary>
public readonly record struct XYPoint : XYGeometry
{
    /// <summary>Creates a Cartesian point.</summary>
    public XYPoint(float x, float y)
    {
        XYGeometryValidation.ValidateCoordinate(x, y);
        X = x;
        Y = y;
    }

    /// <summary>Gets the x coordinate.</summary>
    public float X { get; }

    /// <summary>Gets the y coordinate.</summary>
    public float Y { get; }
}

/// <summary>An immutable Cartesian rectangle.</summary>
public readonly record struct XYRectangle : XYGeometry
{
    /// <summary>Creates a Cartesian rectangle.</summary>
    public XYRectangle(float minX, float minY, float maxX, float maxY)
    {
        XYGeometryValidation.ValidateCoordinate(minX, minY);
        XYGeometryValidation.ValidateCoordinate(maxX, maxY);
        if (minX > maxX) throw new ArgumentOutOfRangeException(nameof(minX), "Rectangle minX must not exceed maxX.");
        if (minY > maxY) throw new ArgumentOutOfRangeException(nameof(minY), "Rectangle minY must not exceed maxY.");
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    /// <summary>Gets the minimum x coordinate.</summary>
    public float MinX { get; }

    /// <summary>Gets the minimum y coordinate.</summary>
    public float MinY { get; }

    /// <summary>Gets the maximum x coordinate.</summary>
    public float MaxX { get; }

    /// <summary>Gets the maximum y coordinate.</summary>
    public float MaxY { get; }
}

/// <summary>An immutable Cartesian circle.</summary>
public readonly record struct XYCircle : XYGeometry
{
    /// <summary>Creates a Cartesian circle.</summary>
    public XYCircle(float x, float y, float radius)
    {
        XYGeometryValidation.ValidateCoordinate(x, y);
        if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius), "Circle radius must be finite and non-negative.");
        X = x;
        Y = y;
        Radius = radius;
    }

    /// <summary>Gets the centre x coordinate.</summary>
    public float X { get; }

    /// <summary>Gets the centre y coordinate.</summary>
    public float Y { get; }

    /// <summary>Gets the radius in coordinate units.</summary>
    public float Radius { get; }
}

/// <summary>An immutable Cartesian line string.</summary>
public sealed class XYLineString : XYGeometry, IEquatable<XYLineString>
{
    private readonly XYPoint[] _points;
    private readonly ReadOnlyCollection<XYPoint> _readOnlyPoints;

    /// <summary>Creates a validated Cartesian line string.</summary>
    public XYLineString(IEnumerable<XYPoint> points)
    {
        _points = XYGeometryValidation.CanonicaliseLine(points, closeRing: false);
        _readOnlyPoints = Array.AsReadOnly(_points);
    }

    /// <summary>Gets the copied canonical point sequence.</summary>
    public IReadOnlyList<XYPoint> Points => _readOnlyPoints;

    /// <inheritdoc />
    public bool Equals(XYLineString? other) => other is not null && _points.AsSpan().SequenceEqual(other._points);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XYLineString other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => XYGeometryValidation.SequenceHash(_points);
}

/// <summary>An immutable Cartesian polygon with one shell and zero or more holes.</summary>
public sealed class XYPolygon : XYGeometry, IEquatable<XYPolygon>
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
        for (int i = 0; i < _holes.Length; i++) holeViews[i] = Array.AsReadOnly(_holes[i]);
        _readOnlyHoles = Array.AsReadOnly(holeViews);
    }

    /// <summary>Gets the canonical closed shell.</summary>
    public IReadOnlyList<XYPoint> Shell => _readOnlyShell;
    /// <summary>Gets the canonical closed holes.</summary>
    public IReadOnlyList<IReadOnlyList<XYPoint>> Holes => _readOnlyHoles;

    /// <inheritdoc />
    public bool Equals(XYPolygon? other)
    {
        if (other is null || !_shell.AsSpan().SequenceEqual(other._shell) || _holes.Length != other._holes.Length) return false;
        for (int i = 0; i < _holes.Length; i++) if (!_holes[i].AsSpan().SequenceEqual(other._holes[i])) return false;
        return true;
    }
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XYPolygon other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(XYGeometryValidation.SequenceHash(_shell));
        foreach (var hole in _holes) hash.Add(XYGeometryValidation.SequenceHash(hole));
        return hash.ToHashCode();
    }
}

/// <summary>An immutable flat collection of non-collection Cartesian geometries.</summary>
public sealed class XYGeometryCollection : XYGeometry, IEquatable<XYGeometryCollection>
{
    private readonly XYGeometry[] _geometries;
    private readonly ReadOnlyCollection<XYGeometry> _readOnlyGeometries;

    /// <summary>Creates a copied, flat Cartesian geometry collection.</summary>
    public XYGeometryCollection(IEnumerable<XYGeometry> geometries)
    {
        ArgumentNullException.ThrowIfNull(geometries);
        _geometries = geometries.ToArray();
        if (_geometries.Length == 0) throw new ArgumentException("A geometry collection must not be empty.", nameof(geometries));
        if (_geometries.Any(static geometry => geometry is null or XYGeometryCollection))
            throw new ArgumentException("Geometry collections must contain non-null non-collection geometries.", nameof(geometries));
        _readOnlyGeometries = Array.AsReadOnly(_geometries);
    }

    /// <summary>Gets the copied flat geometry sequence.</summary>
    public IReadOnlyList<XYGeometry> Geometries => _readOnlyGeometries;
    /// <inheritdoc />
    public bool Equals(XYGeometryCollection? other) => other is not null && _geometries.AsSpan().SequenceEqual(other._geometries);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XYGeometryCollection other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => XYGeometryValidation.SequenceHash(_geometries);
}

internal static class XYGeometryValidation
{
    internal static void ValidateCoordinate(float x, float y)
    {
        if (float.IsNaN(x) || float.IsInfinity(x)) throw new ArgumentOutOfRangeException(nameof(x), "X must be finite.");
        if (float.IsNaN(y) || float.IsInfinity(y)) throw new ArgumentOutOfRangeException(nameof(y), "Y must be finite.");
    }

    internal static XYPoint[] CanonicaliseLine(IEnumerable<XYPoint> source, bool closeRing)
    {
        ArgumentNullException.ThrowIfNull(source);
        var points = CollapseConsecutive(source);
        if (closeRing && points.Count > 1 && points[0].Equals(points[^1])) points.RemoveAt(points.Count - 1);
        if (points.Count < (closeRing ? 3 : 2))
            throw new ArgumentException(closeRing ? "A ring needs at least three effective vertices." : "A line string needs at least two effective points.", nameof(source));
        if (closeRing)
        {
            ValidateRingTopology(points, nameof(source));
            if (SignedArea(points) < 0) points.Reverse();
            points.Add(points[0]);
        }
        return points.ToArray();
    }

    internal static XYPoint[] CanonicaliseRing(IEnumerable<XYPoint> source, bool shellRing)
    {
        ArgumentNullException.ThrowIfNull(source);
        var points = CollapseConsecutive(source);
        if (points.Count > 1 && points[0].Equals(points[^1])) points.RemoveAt(points.Count - 1);
        if (points.Count < 3) throw new ArgumentException("A ring needs at least three effective vertices.", nameof(source));
        ValidateRingTopology(points, nameof(source));
        bool ccw = SignedArea(points) > 0;
        if (shellRing ? !ccw : ccw) points.Reverse();
        points.Add(points[0]);
        return points.ToArray();
    }

    internal static void ValidateHoles(XYPoint[] shell, IReadOnlyList<XYPoint[]> holes)
    {
        var shellOpen = shell[..^1];
        for (int i = 0; i < holes.Count; i++)
        {
            var holeOpen = holes[i][..^1];
            if (!PointInRing(holeOpen[0], shellOpen) || RingsIntersect(shellOpen, holeOpen))
                throw new ArgumentException("A polygon hole must lie inside and not intersect the shell.", nameof(holes));
            for (int j = 0; j < i; j++)
                if (RingsIntersect(holeOpen, holes[j][..^1]) || PointInRing(holeOpen[0], holes[j][..^1]) || PointInRing(holes[j][0], holeOpen))
                    throw new ArgumentException("Polygon holes must not overlap.", nameof(holes));
        }
    }

    internal static int SequenceHash<T>(IEnumerable<T> values)
    {
        var hash = new HashCode();
        foreach (var value in values) hash.Add(value);
        return hash.ToHashCode();
    }

    private static List<XYPoint> CollapseConsecutive(IEnumerable<XYPoint> source)
    {
        var result = new List<XYPoint>();
        foreach (var point in source)
        {
            ValidateCoordinate(point.X, point.Y);
            if (result.Count == 0 || !result[^1].Equals(point)) result.Add(point);
        }
        return result;
    }

    private static void ValidateRingTopology(IReadOnlyList<XYPoint> ring, string parameterName)
    {
        if (MathF.Abs(SignedArea(ring)) <= float.Epsilon) throw new ArgumentException("A polygon ring must have non-zero area.", parameterName);
        for (int i = 0; i < ring.Count; i++)
        {
            for (int j = i + 1; j < ring.Count; j++)
            {
                if (i == j || (i + 1) % ring.Count == j || (j + 1) % ring.Count == i) continue;
                if (SegmentsIntersect(ring[i], ring[(i + 1) % ring.Count], ring[j], ring[(j + 1) % ring.Count]))
                    throw new ArgumentException("A polygon ring must not self-intersect.", parameterName);
            }
        }
    }

    private static bool RingsIntersect(IReadOnlyList<XYPoint> first, IReadOnlyList<XYPoint> second)
    {
        for (int i = 0; i < first.Count; i++)
            for (int j = 0; j < second.Count; j++)
                if (SegmentsIntersect(first[i], first[(i + 1) % first.Count], second[j], second[(j + 1) % second.Count])) return true;
        return false;
    }

    private static bool PointInRing(XYPoint point, IReadOnlyList<XYPoint> ring)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i]; var b = ring[j];
            if (((a.Y > point.Y) != (b.Y > point.Y)) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    private static bool SegmentsIntersect(XYPoint a, XYPoint b, XYPoint c, XYPoint d)
    {
        float abC = Orientation(a, b, c), abD = Orientation(a, b, d), cdA = Orientation(c, d, a), cdB = Orientation(c, d, b);
        const float epsilon = 1e-6f;
        if (MathF.Abs(abC) <= epsilon && OnSegment(a, b, c)) return true;
        if (MathF.Abs(abD) <= epsilon && OnSegment(a, b, d)) return true;
        if (MathF.Abs(cdA) <= epsilon && OnSegment(c, d, a)) return true;
        if (MathF.Abs(cdB) <= epsilon && OnSegment(c, d, b)) return true;
        return ((abC > 0) != (abD > 0)) && ((cdA > 0) != (cdB > 0));
    }

    private static float Orientation(XYPoint a, XYPoint b, XYPoint c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static bool OnSegment(XYPoint a, XYPoint b, XYPoint p) => p.X >= MathF.Min(a.X, b.X) && p.X <= MathF.Max(a.X, b.X) && p.Y >= MathF.Min(a.Y, b.Y) && p.Y <= MathF.Max(a.Y, b.Y);
    private static float SignedArea(IReadOnlyList<XYPoint> ring)
    {
        float area = 0;
        for (int i = 0; i < ring.Count; i++) { var a = ring[i]; var b = ring[(i + 1) % ring.Count]; area += a.X * b.Y - b.X * a.Y; }
        return area / 2;
    }
}

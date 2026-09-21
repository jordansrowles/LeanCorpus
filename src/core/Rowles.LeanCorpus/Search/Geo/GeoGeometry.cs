using System.Collections.ObjectModel;

namespace Rowles.LeanCorpus.Search.Geo;

/// <summary>Marker interface for validated geographic geometry.</summary>
public interface GeoGeometry
{
}

/// <summary>An immutable geographic point in latitude/longitude order.</summary>
public readonly record struct GeoPoint : GeoGeometry
{
    /// <summary>Creates a geographic point.</summary>
    public GeoPoint(double latitude, double longitude)
    {
        GeoGeometryValidation.ValidateCoordinate(latitude, longitude);
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>Gets the latitude in degrees.</summary>
    public double Latitude { get; }

    /// <summary>Gets the longitude in degrees.</summary>
    public double Longitude { get; }
}

/// <summary>An immutable geographic rectangle. West greater than east crosses the dateline.</summary>
public readonly record struct GeoRectangle : GeoGeometry
{
    /// <summary>Creates a geographic rectangle.</summary>
    public GeoRectangle(double south, double west, double north, double east)
    {
        GeoGeometryValidation.ValidateCoordinate(south, west);
        GeoGeometryValidation.ValidateCoordinate(north, east);
        if (south > north)
            throw new ArgumentOutOfRangeException(nameof(south), "Rectangle south must not exceed north.");

        South = south;
        West = west;
        North = north;
        East = east;
    }

    /// <summary>Gets the southern latitude.</summary>
    public double South { get; }

    /// <summary>Gets the western longitude.</summary>
    public double West { get; }

    /// <summary>Gets the northern latitude.</summary>
    public double North { get; }

    /// <summary>Gets the eastern longitude.</summary>
    public double East { get; }

    /// <summary>Gets whether the rectangle crosses the international dateline.</summary>
    public bool CrossesDateline => West > East;
}

/// <summary>An immutable geographic circle with a radius in metres.</summary>
public readonly record struct GeoCircle : GeoGeometry
{
    /// <summary>Creates a geographic circle.</summary>
    public GeoCircle(double latitude, double longitude, double radiusMetres)
    {
        GeoGeometryValidation.ValidateCoordinate(latitude, longitude);
        if (double.IsNaN(radiusMetres) || double.IsInfinity(radiusMetres) || radiusMetres < 0)
            throw new ArgumentOutOfRangeException(nameof(radiusMetres), "Circle radius must be finite and non-negative.");

        Latitude = latitude;
        Longitude = longitude;
        RadiusMetres = radiusMetres;
    }

    /// <summary>Gets the centre latitude.</summary>
    public double Latitude { get; }

    /// <summary>Gets the centre longitude.</summary>
    public double Longitude { get; }

    /// <summary>Gets the radius in metres.</summary>
    public double RadiusMetres { get; }
}

/// <summary>An immutable geographic line string with canonical dateline seam points.</summary>
public sealed class GeoLineString : GeoGeometry, IEquatable<GeoLineString>
{
    private readonly GeoPoint[] _points;
    private readonly ReadOnlyCollection<GeoPoint> _readOnlyPoints;

    /// <summary>Creates and canonicalises a geographic line string.</summary>
    public GeoLineString(IEnumerable<GeoPoint> points)
    {
        _points = GeoGeometryValidation.CanonicaliseLine(points, closeRing: false);
        _readOnlyPoints = Array.AsReadOnly(_points);
    }

    /// <summary>Gets the copied canonical point sequence.</summary>
    public IReadOnlyList<GeoPoint> Points => _readOnlyPoints;

    /// <inheritdoc />
    public bool Equals(GeoLineString? other)
        => other is not null && _points.AsSpan().SequenceEqual(other._points);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GeoLineString other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => GeoGeometryValidation.SequenceHash(_points);
}

/// <summary>An immutable geographic polygon with one shell and zero or more holes.</summary>
public sealed class GeoPolygon : GeoGeometry, IEquatable<GeoPolygon>
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
        var shellPoints = GeoGeometryValidation.CanonicaliseRing(shell, shellRing: true);
        var holePoints = new List<GeoPoint[]>();
        if (holes is not null)
        {
            foreach (var hole in holes)
                holePoints.Add(GeoGeometryValidation.CanonicaliseRing(hole, shellRing: false));
        }

        GeoGeometryValidation.ValidateHoles(shellPoints, holePoints);
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

/// <summary>An immutable flat collection of non-collection geographic geometries.</summary>
public sealed class GeoGeometryCollection : GeoGeometry, IEquatable<GeoGeometryCollection>
{
    private readonly GeoGeometry[] _geometries;
    private readonly ReadOnlyCollection<GeoGeometry> _readOnlyGeometries;

    /// <summary>Creates a copied, flat geographic geometry collection.</summary>
    public GeoGeometryCollection(IEnumerable<GeoGeometry> geometries)
    {
        ArgumentNullException.ThrowIfNull(geometries);
        _geometries = geometries.ToArray();
        if (_geometries.Length == 0)
            throw new ArgumentException("A geometry collection must not be empty.", nameof(geometries));
        if (_geometries.Any(static geometry => geometry is null or GeoGeometryCollection))
            throw new ArgumentException("Geometry collections must contain non-null non-collection geometries.", nameof(geometries));
        _readOnlyGeometries = Array.AsReadOnly(_geometries);
    }

    /// <summary>Gets the copied flat geometry sequence.</summary>
    public IReadOnlyList<GeoGeometry> Geometries => _readOnlyGeometries;

    /// <inheritdoc />
    public bool Equals(GeoGeometryCollection? other)
        => other is not null && _geometries.AsSpan().SequenceEqual(other._geometries);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GeoGeometryCollection other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => GeoGeometryValidation.SequenceHash(_geometries);
}

internal static class GeoGeometryValidation
{
    private readonly record struct GeoCoordinate(double Latitude, double Longitude);

    internal static void ValidateCoordinate(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || double.IsInfinity(latitude) || latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be finite and between -90 and 90 degrees.");
        if (double.IsNaN(longitude) || double.IsInfinity(longitude) || longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be finite and between -180 and 180 degrees.");
    }

    internal static GeoPoint[] CanonicaliseLine(IEnumerable<GeoPoint> source, bool closeRing)
    {
        ArgumentNullException.ThrowIfNull(source);
        var points = CollapseConsecutive(source);
        if (closeRing && points.Count > 1 && points[0].Equals(points[^1]))
            points.RemoveAt(points.Count - 1);
        if (points.Count < (closeRing ? 3 : 2))
            throw new ArgumentException(closeRing ? "A ring needs at least three effective vertices." : "A line string needs at least two effective points.", nameof(source));

        var unwrapped = Unwrap(points);
        if (closeRing)
        {
            ValidateRingTopology(unwrapped, nameof(source));
            if (SignedArea(unwrapped) < 0)
                points.Reverse();
        }

        var canonical = ExpandDateline(points, closeRing);
        if (closeRing)
        {
            if (!canonical[0].Equals(canonical[^1]))
                canonical.Add(canonical[0]);
            if (canonical.Count < 4)
                throw new ArgumentException("A canonical ring needs at least three effective vertices.", nameof(source));
            ValidateEncodedRing(canonical, nameof(source));
        }
        else
        {
            ValidateEncodedLine(canonical, nameof(source));
        }
        return canonical.ToArray();
    }

    internal static GeoPoint[] CanonicaliseRing(IEnumerable<GeoPoint> source, bool shellRing)
    {
        ArgumentNullException.ThrowIfNull(source);
        var points = CollapseConsecutive(source);
        if (points.Count > 1 && points[0].Equals(points[^1]))
            points.RemoveAt(points.Count - 1);
        if (points.Count < 3)
            throw new ArgumentException("A ring needs at least three effective vertices.", nameof(source));

        var unwrapped = Unwrap(points);
        ValidateRingTopology(unwrapped, nameof(source));
        bool shellIsCounterClockwise = SignedArea(unwrapped) > 0;
        if (shellRing ? !shellIsCounterClockwise : shellIsCounterClockwise)
            points.Reverse();
        var canonical = ExpandDateline(points, closeRing: true);
        if (!canonical[0].Equals(canonical[^1]))
            canonical.Add(canonical[0]);
        ValidateEncodedRing(canonical, nameof(source));
        return canonical.ToArray();
    }

    internal static void ValidateHoles(GeoPoint[] shell, IReadOnlyList<GeoPoint[]> holes)
    {
        var shellOpen = ToCoordinates(shell[..^1]);
        for (int i = 0; i < holes.Count; i++)
        {
            var holeOpen = ToCoordinates(holes[i][..^1]);
            if (!PointInRing(holeOpen[0], shellOpen))
                throw new ArgumentException("A polygon hole must lie inside the shell.", nameof(holes));
            if (RingsIntersect(shellOpen, holeOpen))
                throw new ArgumentException("A polygon hole must not intersect the shell.", nameof(holes));
            for (int j = 0; j < i; j++)
            {
                var previousHole = ToCoordinates(holes[j][..^1]);
                if (RingsIntersect(holeOpen, previousHole) || PointInRing(holeOpen[0], previousHole) || PointInRing(previousHole[0], holeOpen))
                    throw new ArgumentException("Polygon holes must not overlap.", nameof(holes));
            }
        }
    }

    internal static int SequenceHash<T>(IEnumerable<T> values)
    {
        var hash = new HashCode();
        foreach (var value in values)
            hash.Add(value);
        return hash.ToHashCode();
    }

    private static List<GeoPoint> CollapseConsecutive(IEnumerable<GeoPoint> source)
    {
        var result = new List<GeoPoint>();
        foreach (var point in source)
        {
            ValidateCoordinate(point.Latitude, point.Longitude);
            if (result.Count == 0 || !result[^1].Equals(point))
                result.Add(point);
        }
        return result;
    }

    private static List<GeoCoordinate> ToCoordinates(IReadOnlyList<GeoPoint> points)
    {
        var result = new List<GeoCoordinate>(points.Count);
        for (int i = 0; i < points.Count; i++)
            result.Add(new GeoCoordinate(points[i].Latitude, points[i].Longitude));
        return result;
    }

    private static List<GeoPoint> ExpandDateline(IReadOnlyList<GeoPoint> points, bool closeRing)
    {
        var result = new List<GeoPoint>(points.Count + 2);
        result.Add(points[0]);
        double previousUnwrapped = points[0].Longitude;
        int edgeCount = closeRing ? points.Count : points.Count - 1;
        for (int edge = 1; edge <= edgeCount; edge++)
        {
            var point = points[edge % points.Count];
            double previousNormalised = GeoEncodingUtils.NormalizeLongitude(previousUnwrapped);
            double target = UnwrapNear(previousUnwrapped, point.Longitude);
            double rawDifference = point.Longitude - previousNormalised;
            double difference = target - previousUnwrapped;
            if (Math.Abs(rawDifference) > 360 || Math.Abs(difference) > 360)
                throw new ArgumentException("A geometry edge must not contain a complete world wrap.", nameof(points));
            if (difference != 0 && rawDifference < -180)
            {
                double latitude = points[(edge - 1) % points.Count].Latitude +
                    (point.Latitude - points[(edge - 1) % points.Count].Latitude) * ((180 - previousNormalised) / difference);
                result.Add(new GeoPoint(latitude, 180));
                result.Add(new GeoPoint(latitude, -180));
            }
            else if (difference != 0 && rawDifference > 180)
            {
                double latitude = points[(edge - 1) % points.Count].Latitude +
                    (point.Latitude - points[(edge - 1) % points.Count].Latitude) * ((-180 - previousNormalised) / difference);
                result.Add(new GeoPoint(latitude, -180));
                result.Add(new GeoPoint(latitude, 180));
            }

            var normalised = new GeoPoint(point.Latitude, GeoEncodingUtils.NormalizeLongitude(point.Longitude));
            if (!result[^1].Equals(normalised))
                result.Add(normalised);
            previousUnwrapped = target;
        }

        return result;
    }

    private static List<GeoCoordinate> Unwrap(IReadOnlyList<GeoPoint> points)
    {
        var result = new List<GeoCoordinate>(points.Count);
        double previous = points[0].Longitude;
        result.Add(new GeoCoordinate(points[0].Latitude, previous));
        for (int i = 1; i < points.Count; i++)
        {
            double longitude = UnwrapNear(previous, points[i].Longitude);
            result.Add(new GeoCoordinate(points[i].Latitude, longitude));
            previous = longitude;
        }
        return result;
    }

    private static double UnwrapNear(double previous, double longitude)
    {
        while (longitude - previous > 180) longitude -= 360;
        while (longitude - previous < -180) longitude += 360;
        return longitude;
    }

    private static void ValidateRingTopology(IReadOnlyList<GeoCoordinate> ring, string parameterName)
    {
        if (Math.Abs(SignedArea(ring)) <= double.Epsilon)
            throw new ArgumentException("A polygon ring must have non-zero area.", parameterName);
        for (int i = 0; i < ring.Count; i++)
        {
            var a1 = ring[i];
            var a2 = ring[(i + 1) % ring.Count];
            for (int j = i + 1; j < ring.Count; j++)
            {
                int next = (j + 1) % ring.Count;
                if (i == j || (i + 1) % ring.Count == j || next == i)
                    continue;
                if (SegmentsIntersect(a1, a2, ring[j], ring[next]))
                    throw new ArgumentException("A polygon ring must not self-intersect.", parameterName);
            }
        }
    }

    private static bool RingsIntersect(IReadOnlyList<GeoCoordinate> first, IReadOnlyList<GeoCoordinate> second)
    {
        for (int i = 0; i < first.Count; i++)
        {
            var a1 = first[i];
            var a2 = first[(i + 1) % first.Count];
            for (int j = 0; j < second.Count; j++)
                if (SegmentsIntersect(a1, a2, second[j], second[(j + 1) % second.Count]))
                    return true;
        }
        return false;
    }

    private static bool PointInRing(GeoCoordinate point, IReadOnlyList<GeoCoordinate> ring)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if (((a.Latitude > point.Latitude) != (b.Latitude > point.Latitude)) &&
                point.Longitude < (b.Longitude - a.Longitude) * (point.Latitude - a.Latitude) / (b.Latitude - a.Latitude) + a.Longitude)
                inside = !inside;
        }
        return inside;
    }

    private static bool SegmentsIntersect(GeoCoordinate a, GeoCoordinate b, GeoCoordinate c, GeoCoordinate d)
    {
        double abC = Orientation(a, b, c);
        double abD = Orientation(a, b, d);
        double cdA = Orientation(c, d, a);
        double cdB = Orientation(c, d, b);
        const double epsilon = 1e-12;
        if (Math.Abs(abC) <= epsilon && OnSegment(a, b, c)) return true;
        if (Math.Abs(abD) <= epsilon && OnSegment(a, b, d)) return true;
        if (Math.Abs(cdA) <= epsilon && OnSegment(c, d, a)) return true;
        if (Math.Abs(cdB) <= epsilon && OnSegment(c, d, b)) return true;
        return ((abC > 0) != (abD > 0)) && ((cdA > 0) != (cdB > 0));
    }

    private static double Orientation(GeoCoordinate a, GeoCoordinate b, GeoCoordinate c)
        => (b.Longitude - a.Longitude) * (c.Latitude - a.Latitude) -
           (b.Latitude - a.Latitude) * (c.Longitude - a.Longitude);

    private static bool OnSegment(GeoCoordinate a, GeoCoordinate b, GeoCoordinate point)
        => point.Longitude >= Math.Min(a.Longitude, b.Longitude) && point.Longitude <= Math.Max(a.Longitude, b.Longitude) &&
           point.Latitude >= Math.Min(a.Latitude, b.Latitude) && point.Latitude <= Math.Max(a.Latitude, b.Latitude);

    private static double SignedArea(IReadOnlyList<GeoCoordinate> ring)
    {
        double area = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            var current = ring[i];
            var next = ring[(i + 1) % ring.Count];
            area += current.Longitude * next.Latitude - next.Longitude * current.Latitude;
        }
        return area / 2;
    }

    private static void ValidateEncodedLine(IReadOnlyList<GeoPoint> points, string parameterName)
    {
        int distinct = 0;
        int previousLat = 0;
        int previousLon = 0;
        bool hasPrevious = false;
        foreach (var point in points)
        {
            int latitude = GeoEncodingUtils.EncodeLat(point.Latitude);
            int longitude = GeoEncodingUtils.EncodeLon(point.Longitude);
            if (!hasPrevious || latitude != previousLat || longitude != previousLon)
            {
                distinct++;
                previousLat = latitude;
                previousLon = longitude;
                hasPrevious = true;
            }
        }
        if (distinct < 2)
            throw new ArgumentException("A line string must remain non-degenerate after coordinate encoding.", parameterName);
    }

    private static void ValidateEncodedRing(IReadOnlyList<GeoPoint> points, string parameterName)
    {
        const long encodedWorld = 1L << 32;
        const long encodedHalfWorld = 1L << 31;
        var encoded = new List<(long Latitude, long Longitude)>(points.Count - 1);
        long previousLongitude = 0;
        bool hasPreviousLongitude = false;
        for (int i = 0; i < points.Count - 1; i++)
        {
            int latitude = GeoEncodingUtils.EncodeLat(points[i].Latitude);
            uint sortableLongitude = unchecked((uint)(GeoEncodingUtils.EncodeLon(points[i].Longitude) ^ int.MinValue));
            long longitude = sortableLongitude;
            if (hasPreviousLongitude)
            {
                while (longitude - previousLongitude > encodedHalfWorld)
                    longitude -= encodedWorld;
                while (longitude - previousLongitude < -encodedHalfWorld)
                    longitude += encodedWorld;
            }
            previousLongitude = longitude;
            hasPreviousLongitude = true;
            var value = ((long)latitude, longitude);
            if (encoded.Count == 0 || encoded[^1] != value)
                encoded.Add(value);
        }
        if (encoded.Count > 1 && encoded[0] == encoded[^1])
            encoded.RemoveAt(encoded.Count - 1);
        if (encoded.Count < 3)
            throw new ArgumentException("A polygon ring must remain non-degenerate after coordinate encoding.", parameterName);

        double area = 0;
        for (int i = 0; i < encoded.Count; i++)
        {
            var current = encoded[i];
            var next = encoded[(i + 1) % encoded.Count];
            area += (double)current.Longitude * next.Latitude - (double)next.Longitude * current.Latitude;
        }
        if (area == 0)
            throw new ArgumentException("A polygon ring must retain non-zero encoded area.", parameterName);

        for (int i = 0; i < encoded.Count; i++)
        {
            for (int j = i + 1; j < encoded.Count; j++)
            {
                int next = (j + 1) % encoded.Count;
                if ((i + 1) % encoded.Count == j || next == i)
                    continue;
                if (EncodedSegmentsIntersect(encoded[i], encoded[(i + 1) % encoded.Count], encoded[j], encoded[next]))
                    throw new ArgumentException("A polygon ring must not self-intersect after coordinate encoding.", parameterName);
            }
        }
    }

    private static bool EncodedSegmentsIntersect(
        (long Latitude, long Longitude) first,
        (long Latitude, long Longitude) second,
        (long Latitude, long Longitude) third,
        (long Latitude, long Longitude) fourth)
    {
        double firstThird = EncodedOrientation(first, second, third);
        double firstFourth = EncodedOrientation(first, second, fourth);
        double thirdFirst = EncodedOrientation(third, fourth, first);
        double thirdSecond = EncodedOrientation(third, fourth, second);
        if (firstThird == 0 && EncodedOnSegment(first, second, third)) return true;
        if (firstFourth == 0 && EncodedOnSegment(first, second, fourth)) return true;
        if (thirdFirst == 0 && EncodedOnSegment(third, fourth, first)) return true;
        if (thirdSecond == 0 && EncodedOnSegment(third, fourth, second)) return true;
        return ((firstThird > 0) != (firstFourth > 0)) && ((thirdFirst > 0) != (thirdSecond > 0));
    }

    private static double EncodedOrientation(
        (long Latitude, long Longitude) first,
        (long Latitude, long Longitude) second,
        (long Latitude, long Longitude) third)
        => ((double)second.Longitude - first.Longitude) * (third.Latitude - first.Latitude)
           - ((double)second.Latitude - first.Latitude) * (third.Longitude - first.Longitude);

    private static bool EncodedOnSegment(
        (long Latitude, long Longitude) first,
        (long Latitude, long Longitude) second,
        (long Latitude, long Longitude) point)
        => point.Longitude >= Math.Min(first.Longitude, second.Longitude)
           && point.Longitude <= Math.Max(first.Longitude, second.Longitude)
           && point.Latitude >= Math.Min(first.Latitude, second.Latitude)
           && point.Latitude <= Math.Max(first.Latitude, second.Latitude);
}

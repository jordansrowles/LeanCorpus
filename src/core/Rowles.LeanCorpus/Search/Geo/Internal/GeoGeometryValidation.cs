namespace Rowles.LeanCorpus.Search.Geo.Internal;

internal static class GeoGeometryValidation
{
    private readonly record struct GeoCoordinate(double Latitude, double Longitude);
    private readonly record struct EncodedCoordinate(long Latitude, long Longitude);

    internal static void ValidateCoordinate(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || double.IsInfinity(latitude) || latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be finite and between -90 and 90 degrees.");
        if (double.IsNaN(longitude) || double.IsInfinity(longitude) || longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be finite and between -180 and 180 degrees.");
    }

    internal static GeoPoint[] CanonicaliseLine(IEnumerable<GeoPoint> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var points = CollapseConsecutive(source);
        CollapseDatelineSeamPairs(points);
        if (points.Count < 2)
            throw new ArgumentException("A line string needs at least two effective points.", nameof(source));

        _ = Unwrap(points, closeRing: false, nameof(source));
        var canonical = ExpandDateline(points, closeRing: false);
        ValidateEncodedLine(canonical, nameof(source));
        return canonical.ToArray();
    }

    internal static void CanonicalisePolygon(
        IEnumerable<GeoPoint> shell,
        IEnumerable<IEnumerable<GeoPoint>>? holes,
        out GeoPoint[] canonicalShell,
        out List<GeoPoint[]> canonicalHoles)
    {
        var shellPoints = PrepareRing(shell, nameof(shell));
        var shellUnwrapped = Unwrap(shellPoints, closeRing: true, nameof(shell));
        ValidateRingTopology(shellUnwrapped, nameof(shell));

        var holePoints = new List<List<GeoPoint>>();
        var holeUnwrapped = new List<List<GeoCoordinate>>();
        if (holes is not null)
        {
            foreach (var hole in holes)
            {
                var points = PrepareRing(hole, nameof(holes));
                holePoints.Add(points);
                var unwrapped = Unwrap(points, closeRing: true, nameof(holes), shellUnwrapped[0].Longitude);
                ValidateRingTopology(unwrapped, nameof(holes));
                holeUnwrapped.Add(unwrapped);
            }
        }

        ValidateLogicalHoles(shellUnwrapped, holeUnwrapped);

        if (SignedArea(shellUnwrapped) < 0)
        {
            shellPoints.Reverse();
            shellUnwrapped.Reverse();
        }

        for (int i = 0; i < holePoints.Count; i++)
        {
            if (SignedArea(holeUnwrapped[i]) > 0)
            {
                holePoints[i].Reverse();
                holeUnwrapped[i].Reverse();
            }
        }

        ValidateEncodedPolygon(shellUnwrapped, holeUnwrapped, nameof(shell));

        var expandedShell = ExpandDateline(shellPoints, closeRing: true);
        if (!expandedShell[0].Equals(expandedShell[^1]))
            expandedShell.Add(expandedShell[0]);
        ValidateEncodedRing(expandedShell, nameof(shell));
        canonicalShell = expandedShell.ToArray();

        canonicalHoles = new List<GeoPoint[]>(holePoints.Count);
        foreach (var points in holePoints)
        {
            var expandedHole = ExpandDateline(points, closeRing: true);
            if (!expandedHole[0].Equals(expandedHole[^1]))
                expandedHole.Add(expandedHole[0]);
            ValidateEncodedRing(expandedHole, nameof(holes));
            canonicalHoles.Add(expandedHole.ToArray());
        }
    }

    internal static int SequenceHash<T>(IEnumerable<T> values)
    {
        var hash = new HashCode();
        foreach (var value in values)
            hash.Add(value);
        return hash.ToHashCode();
    }

    private static List<GeoPoint> PrepareRing(IEnumerable<GeoPoint> source, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(source);
        var points = CollapseConsecutive(source);
        if (points.Count > 1 && points[0].Equals(points[^1]))
            points.RemoveAt(points.Count - 1);
        CollapseDatelineSeamPairs(points);
        if (points.Count < 3)
            throw new ArgumentException("A ring needs at least three effective vertices.", parameterName);
        return points;
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

    private static void CollapseDatelineSeamPairs(List<GeoPoint> points)
    {
        for (int i = 0; i + 1 < points.Count;)
        {
            GeoPoint first = points[i];
            GeoPoint second = points[i + 1];
            bool isSeamPair = first.Latitude == second.Latitude &&
                ((first.Longitude == 180 && second.Longitude == -180) ||
                 (first.Longitude == -180 && second.Longitude == 180));
            if (!isSeamPair)
            {
                i++;
                continue;
            }

            points.RemoveRange(i, 2);
            if (i > 0)
                i--;
        }
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
            double previousNormalised = GeoEncodingUtils.NormaliseLongitude(previousUnwrapped);
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

            var normalised = new GeoPoint(point.Latitude, GeoEncodingUtils.NormaliseLongitude(point.Longitude));
            if (!result[^1].Equals(normalised))
                result.Add(normalised);
            previousUnwrapped = target;
        }

        return result;
    }

    private static List<GeoCoordinate> Unwrap(
        IReadOnlyList<GeoPoint> points,
        bool closeRing,
        string parameterName,
        double? referenceLongitude = null)
    {
        const double tolerance = 1e-12;
        var result = new List<GeoCoordinate>(points.Count);
        double firstLongitude = referenceLongitude.HasValue
            ? UnwrapNear(referenceLongitude.Value, points[0].Longitude)
            : points[0].Longitude;
        double previous = firstLongitude;
        double minimum = firstLongitude;
        double maximum = firstLongitude;
        result.Add(new GeoCoordinate(points[0].Latitude, firstLongitude));
        for (int i = 1; i < points.Count; i++)
        {
            double longitude = UnwrapNear(previous, points[i].Longitude);
            result.Add(new GeoCoordinate(points[i].Latitude, longitude));
            previous = longitude;
            minimum = Math.Min(minimum, longitude);
            maximum = Math.Max(maximum, longitude);
        }

        if (maximum - minimum > 360 + tolerance)
            throw new ArgumentException("A geometry must not contain more than one world of longitude.", parameterName);

        if (closeRing)
        {
            double closure = UnwrapNear(previous, points[0].Longitude);
            if (Math.Abs(closure - firstLongitude) > tolerance)
                throw new ArgumentException("A polygon ring must close in its starting world.", parameterName);
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
        if (Math.Abs(SignedArea(ring)) <= 1e-12)
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

    private static void ValidateLogicalHoles(
        IReadOnlyList<GeoCoordinate> shell,
        IReadOnlyList<List<GeoCoordinate>> holes)
    {
        for (int i = 0; i < holes.Count; i++)
        {
            var hole = holes[i];
            if (!PointInRing(hole[0], shell))
                throw new ArgumentException("A polygon hole must lie inside the shell.", nameof(holes));
            if (RingsIntersect(shell, hole))
                throw new ArgumentException("A polygon hole must not intersect the shell.", nameof(holes));
            for (int j = 0; j < i; j++)
            {
                var previousHole = holes[j];
                if (RingsIntersect(hole, previousHole) || PointInRing(hole[0], previousHole) || PointInRing(previousHole[0], hole))
                    throw new ArgumentException("Polygon holes must not overlap.", nameof(holes));
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
        var unwrapped = Unwrap(points.Take(points.Count - 1).ToArray(), closeRing: true, parameterName);
        ValidateEncodedRing(ToEncodedRing(unwrapped, parameterName), parameterName);
    }

    private static void ValidateEncodedPolygon(
        IReadOnlyList<GeoCoordinate> shell,
        IReadOnlyList<List<GeoCoordinate>> holes,
        string parameterName)
    {
        var encodedShell = ToEncodedRing(shell, parameterName);
        ValidateEncodedRing(encodedShell, parameterName);
        var encodedHoles = new List<List<EncodedCoordinate>>(holes.Count);
        foreach (var hole in holes)
        {
            var encodedHole = ToEncodedRing(hole, nameof(holes));
            ValidateEncodedRing(encodedHole, nameof(holes));
            encodedHoles.Add(encodedHole);
        }

        for (int i = 0; i < encodedHoles.Count; i++)
        {
            var hole = encodedHoles[i];
            if (!PointInRing(hole[0], encodedShell) || RingsIntersect(encodedShell, hole))
                throw new ArgumentException("A polygon hole must remain inside and not intersect the shell after coordinate encoding.", parameterName);
            for (int j = 0; j < i; j++)
            {
                var previousHole = encodedHoles[j];
                if (RingsIntersect(hole, previousHole) || PointInRing(hole[0], previousHole) || PointInRing(previousHole[0], hole))
                    throw new ArgumentException("Polygon holes must not overlap after coordinate encoding.", parameterName);
            }
        }
    }

    private static List<EncodedCoordinate> ToEncodedRing(
        IReadOnlyList<GeoCoordinate> ring,
        string parameterName)
    {
        var encoded = new List<EncodedCoordinate>(ring.Count);
        foreach (var coordinate in ring)
        {
            var value = new EncodedCoordinate(
                GeoEncodingUtils.EncodeLat(coordinate.Latitude),
                EncodeUnwrappedLongitude(coordinate.Longitude));
            if (encoded.Count == 0 || encoded[^1] != value)
                encoded.Add(value);
        }

        if (encoded.Count > 1 && encoded[0] == encoded[^1])
            encoded.RemoveAt(encoded.Count - 1);
        if (encoded.Count < 3)
            throw new ArgumentException("A polygon ring must remain non-degenerate after coordinate encoding.", parameterName);
        return encoded;
    }

    private static long EncodeUnwrappedLongitude(double longitude)
    {
        const long encodedWorld = 1L << 32;
        double normalised = GeoEncodingUtils.NormaliseLongitude(longitude);
        long worldOffset = checked((long)Math.Round((longitude - normalised) / 360.0, MidpointRounding.ToEven)) * encodedWorld;
        long encoded = unchecked((uint)(GeoEncodingUtils.EncodeLon(normalised) ^ int.MinValue));
        return checked(worldOffset + encoded);
    }

    private static void ValidateEncodedRing(IReadOnlyList<EncodedCoordinate> ring, string parameterName)
    {
        Int128 area = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            var current = ring[i];
            var next = ring[(i + 1) % ring.Count];
            area += (Int128)current.Longitude * next.Latitude - (Int128)next.Longitude * current.Latitude;
        }
        if (area == 0)
            throw new ArgumentException("A polygon ring must retain non-zero encoded area.", parameterName);

        for (int i = 0; i < ring.Count; i++)
        {
            for (int j = i + 1; j < ring.Count; j++)
            {
                int next = (j + 1) % ring.Count;
                if ((i + 1) % ring.Count == j || next == i)
                    continue;
                if (EncodedSegmentsIntersect(ring[i], ring[(i + 1) % ring.Count], ring[j], ring[next]))
                    throw new ArgumentException("A polygon ring must not self-intersect after coordinate encoding.", parameterName);
            }
        }
    }

    private static bool RingsIntersect(IReadOnlyList<EncodedCoordinate> first, IReadOnlyList<EncodedCoordinate> second)
    {
        for (int i = 0; i < first.Count; i++)
        {
            var firstStart = first[i];
            var firstEnd = first[(i + 1) % first.Count];
            for (int j = 0; j < second.Count; j++)
                if (EncodedSegmentsIntersect(firstStart, firstEnd, second[j], second[(j + 1) % second.Count]))
                    return true;
        }
        return false;
    }

    private static bool PointInRing(EncodedCoordinate point, IReadOnlyList<EncodedCoordinate> ring)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Latitude > point.Latitude) == (b.Latitude > point.Latitude))
                continue;

            Int128 left = (Int128)(point.Longitude - a.Longitude) * (b.Latitude - a.Latitude);
            Int128 right = (Int128)(b.Longitude - a.Longitude) * (point.Latitude - a.Latitude);
            bool crossesToRight = b.Latitude > a.Latitude ? left < right : left > right;
            if (crossesToRight)
                inside = !inside;
        }
        return inside;
    }

    private static bool EncodedSegmentsIntersect(
        EncodedCoordinate first,
        EncodedCoordinate second,
        EncodedCoordinate third,
        EncodedCoordinate fourth)
    {
        Int128 firstThird = EncodedOrientation(first, second, third);
        Int128 firstFourth = EncodedOrientation(first, second, fourth);
        Int128 thirdFirst = EncodedOrientation(third, fourth, first);
        Int128 thirdSecond = EncodedOrientation(third, fourth, second);
        if (firstThird == 0 && EncodedOnSegment(first, second, third)) return true;
        if (firstFourth == 0 && EncodedOnSegment(first, second, fourth)) return true;
        if (thirdFirst == 0 && EncodedOnSegment(third, fourth, first)) return true;
        if (thirdSecond == 0 && EncodedOnSegment(third, fourth, second)) return true;
        return ((firstThird > 0) != (firstFourth > 0)) && ((thirdFirst > 0) != (thirdSecond > 0));
    }

    private static Int128 EncodedOrientation(
        EncodedCoordinate first,
        EncodedCoordinate second,
        EncodedCoordinate third)
        => (Int128)(second.Longitude - first.Longitude) * (third.Latitude - first.Latitude)
           - (Int128)(second.Latitude - first.Latitude) * (third.Longitude - first.Longitude);

    private static bool EncodedOnSegment(
        EncodedCoordinate first,
        EncodedCoordinate second,
        EncodedCoordinate point)
        => point.Longitude >= Math.Min(first.Longitude, second.Longitude)
           && point.Longitude <= Math.Max(first.Longitude, second.Longitude)
           && point.Latitude >= Math.Min(first.Latitude, second.Latitude)
           && point.Latitude <= Math.Max(first.Latitude, second.Latitude);
}

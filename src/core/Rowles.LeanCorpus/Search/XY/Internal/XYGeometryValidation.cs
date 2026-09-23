namespace Rowles.LeanCorpus.Search.XY.Internal;

internal static class XYGeometryValidation
{
    internal static void ValidateCoordinate(float x, float y)
    {
        if (float.IsNaN(x) || float.IsInfinity(x)) throw new ArgumentOutOfRangeException(nameof(x), "X must be finite.");
        if (float.IsNaN(y) || float.IsInfinity(y)) throw new ArgumentOutOfRangeException(nameof(y), "Y must be finite.");
    }

    internal static XYPoint[] CanonicaliseLine(IEnumerable<XYPoint> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var points = CollapseConsecutive(source);
        if (points.Count < 2)
            throw new ArgumentException("A line string needs at least two effective points.", nameof(source));
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
        if (Math.Abs(SignedArea(ring)) <= 1e-12) throw new ArgumentException("A polygon ring must have non-zero area.", parameterName);
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
            if (((a.Y > point.Y) != (b.Y > point.Y)) &&
                (double)point.X < ((double)b.X - a.X) * ((double)point.Y - a.Y) / ((double)b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static bool SegmentsIntersect(XYPoint a, XYPoint b, XYPoint c, XYPoint d)
    {
        double abC = Orientation(a, b, c), abD = Orientation(a, b, d), cdA = Orientation(c, d, a), cdB = Orientation(c, d, b);
        const double epsilon = 1e-12;
        if (Math.Abs(abC) <= epsilon && OnSegment(a, b, c)) return true;
        if (Math.Abs(abD) <= epsilon && OnSegment(a, b, d)) return true;
        if (Math.Abs(cdA) <= epsilon && OnSegment(c, d, a)) return true;
        if (Math.Abs(cdB) <= epsilon && OnSegment(c, d, b)) return true;
        return ((abC > 0) != (abD > 0)) && ((cdA > 0) != (cdB > 0));
    }

    private static double Orientation(XYPoint a, XYPoint b, XYPoint c)
        => ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X);

    private static bool OnSegment(XYPoint a, XYPoint b, XYPoint p)
        => (double)p.X >= Math.Min((double)a.X, b.X) && (double)p.X <= Math.Max((double)a.X, b.X)
           && (double)p.Y >= Math.Min((double)a.Y, b.Y) && (double)p.Y <= Math.Max((double)a.Y, b.Y);

    private static double SignedArea(IReadOnlyList<XYPoint> ring)
    {
        double area = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            area += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return area / 2;
    }
}

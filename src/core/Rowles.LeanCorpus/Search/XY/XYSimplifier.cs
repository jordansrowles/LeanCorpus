using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial;

/// <summary>Simplifies Cartesian lines and polygons with explicit coordinate-unit tolerances.</summary>
public static class XYSimplifier
{
    /// <summary>Simplifies a Cartesian line with deterministic Douglas-Peucker reduction.</summary>
    public static XYLineString Simplify(XYLineString line, float tolerance)
    {
        ArgumentNullException.ThrowIfNull(line);
        ValidateTolerance(tolerance);
        if (tolerance == 0f)
            return line;
        return new XYLineString(SimplifyPath(line.Points.ToArray(), tolerance));
    }

    /// <summary>Simplifies each ring independently and revalidates polygon topology.</summary>
    public static XYPolygon Simplify(XYPolygon polygon, float tolerance)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        ValidateTolerance(tolerance);
        if (tolerance == 0f)
            return polygon;

        XYPoint[] shell = SimplifyRing(polygon.Shell, tolerance);
        var holes = new XYPoint[polygon.Holes.Count][];
        for (int i = 0; i < holes.Length; i++)
            holes[i] = SimplifyRing(polygon.Holes[i], tolerance);

        try
        {
            return new XYPolygon(shell, holes);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("XY polygon simplification produced invalid topology.", exception);
        }
    }

    /// <summary>Simplifies line and polygon components while retaining other components unchanged.</summary>
    public static XYGeometryCollection Simplify(XYGeometryCollection collection, float tolerance)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ValidateTolerance(tolerance);
        if (tolerance == 0f)
            return collection;

        var geometries = new IXYGeometry[collection.Geometries.Count];
        for (int i = 0; i < geometries.Length; i++)
        {
            geometries[i] = collection.Geometries[i] switch
            {
                XYLineString line => Simplify(line, tolerance),
                XYPolygon polygon => Simplify(polygon, tolerance),
                IXYGeometry retained => retained,
            };
        }
        return new XYGeometryCollection(geometries);
    }

    private static void ValidateTolerance(float tolerance)
    {
        if (!float.IsFinite(tolerance) || tolerance < 0f)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "Tolerance must be finite and non-negative.");
    }

    private static XYPoint[] SimplifyRing(IReadOnlyList<XYPoint> closedRing, float tolerance)
    {
        int uniqueCount = closedRing.Count - 1;
        if (uniqueCount < 3 || closedRing[0] != closedRing[^1])
            throw new InvalidOperationException("An XY polygon ring is not a valid explicitly closed ring.");

        XYPoint anchor = closedRing[0];
        int farthestIndex = 1;
        double farthestDistance = -1d;
        for (int i = 1; i < uniqueCount; i++)
        {
            double distance = Distance(anchor, closedRing[i]);
            if (distance > farthestDistance)
            {
                farthestDistance = distance;
                farthestIndex = i;
            }
        }

        XYPoint[] firstArc = closedRing.Take(farthestIndex + 1).ToArray();
        var secondArc = new List<XYPoint>(uniqueCount - farthestIndex + 1);
        for (int i = farthestIndex; i < uniqueCount; i++)
            secondArc.Add(closedRing[i]);
        secondArc.Add(anchor);

        XYPoint[] simplifiedFirst = SimplifyPath(firstArc, tolerance);
        XYPoint[] simplifiedSecond = SimplifyPath(secondArc.ToArray(), tolerance);
        var result = new List<XYPoint>(simplifiedFirst.Length + simplifiedSecond.Length - 1);
        result.AddRange(simplifiedFirst);
        for (int i = 1; i < simplifiedSecond.Length; i++)
            result.Add(simplifiedSecond[i]);

        if (result.Count < 2 || result[0] != result[^1])
            result.Add(result[0]);
        if (result.Take(result.Count - 1).Distinct().Count() < 3)
            throw new InvalidOperationException("XY polygon simplification would leave fewer than three distinct ring vertices.");
        return result.ToArray();
    }

    private static XYPoint[] SimplifyPath(XYPoint[] points, float tolerance)
    {
        if (points.Length <= 2)
            return points;

        var keep = new bool[points.Length];
        keep[0] = true;
        keep[^1] = true;
        var pending = new Stack<(int Start, int End)>();
        pending.Push((0, points.Length - 1));
        while (pending.Count > 0)
        {
            (int start, int end) = pending.Pop();
            double maximumDistance = -1d;
            int selectedIndex = -1;
            for (int i = start + 1; i < end; i++)
            {
                double distance = DistanceToSegment(points[i], points[start], points[end]);
                if (distance > maximumDistance)
                {
                    maximumDistance = distance;
                    selectedIndex = i;
                }
            }

            if (selectedIndex < 0 || maximumDistance <= tolerance)
                continue;

            keep[selectedIndex] = true;
            pending.Push((selectedIndex, end));
            pending.Push((start, selectedIndex));
        }

        var simplified = new List<XYPoint>();
        for (int i = 0; i < points.Length; i++)
            if (keep[i])
                simplified.Add(points[i]);
        return simplified.ToArray();
    }

    private static double DistanceToSegment(XYPoint point, XYPoint start, XYPoint end)
    {
        double deltaX = (double)end.X - start.X;
        double deltaY = (double)end.Y - start.Y;
        double lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (lengthSquared <= 0d)
            return Distance(point, start);

        double projection = ((((double)point.X - start.X) * deltaX)
            + (((double)point.Y - start.Y) * deltaY)) / lengthSquared;
        if (projection <= 0d)
            return Distance(point, start);
        if (projection >= 1d)
            return Distance(point, end);

        double closestX = start.X + (projection * deltaX);
        double closestY = start.Y + (projection * deltaY);
        double differenceX = point.X - closestX;
        double differenceY = point.Y - closestY;
        return Math.Sqrt((differenceX * differenceX) + (differenceY * differenceY));
    }

    private static double Distance(XYPoint left, XYPoint right)
    {
        double deltaX = (double)left.X - right.X;
        double deltaY = (double)left.Y - right.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }
}

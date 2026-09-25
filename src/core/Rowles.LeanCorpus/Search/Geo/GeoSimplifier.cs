using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial;

/// <summary>Simplifies geographic lines and polygons with explicit metre tolerances.</summary>
public static class GeoSimplifier
{
    private const double EarthRadiusMetres = 6_371_000d;

    /// <summary>Simplifies a geographic line with deterministic Douglas-Peucker reduction.</summary>
    public static GeoLineString Simplify(GeoLineString line, double toleranceMetres)
    {
        ArgumentNullException.ThrowIfNull(line);
        ValidateTolerance(toleranceMetres);
        if (toleranceMetres == 0d)
            return line;

        GeoPoint[] points = SimplifyPath(line.Points.ToArray(), toleranceMetres);
        return new GeoLineString(points);
    }

    /// <summary>Simplifies each ring independently and revalidates polygon topology.</summary>
    public static GeoPolygon Simplify(GeoPolygon polygon, double toleranceMetres)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        ValidateTolerance(toleranceMetres);
        if (toleranceMetres == 0d)
            return polygon;

        GeoPoint[] shell = SimplifyRing(polygon.Shell, toleranceMetres);
        var holes = new GeoPoint[polygon.Holes.Count][];
        for (int i = 0; i < holes.Length; i++)
            holes[i] = SimplifyRing(polygon.Holes[i], toleranceMetres);

        try
        {
            return new GeoPolygon(shell, holes);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("Geo polygon simplification produced invalid topology.", exception);
        }
    }

    /// <summary>Simplifies line and polygon components while retaining other components unchanged.</summary>
    public static GeoGeometryCollection Simplify(GeoGeometryCollection collection, double toleranceMetres)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ValidateTolerance(toleranceMetres);
        if (toleranceMetres == 0d)
            return collection;

        var geometries = new IGeoGeometry[collection.Geometries.Count];
        for (int i = 0; i < geometries.Length; i++)
        {
            geometries[i] = collection.Geometries[i] switch
            {
                GeoLineString line => Simplify(line, toleranceMetres),
                GeoPolygon polygon => Simplify(polygon, toleranceMetres),
                IGeoGeometry retained => retained,
            };
        }
        return new GeoGeometryCollection(geometries);
    }

    private static void ValidateTolerance(double toleranceMetres)
    {
        if (!double.IsFinite(toleranceMetres) || toleranceMetres < 0d)
            throw new ArgumentOutOfRangeException(nameof(toleranceMetres), "Tolerance must be finite and non-negative.");
    }

    private static GeoPoint[] SimplifyRing(IReadOnlyList<GeoPoint> closedRing, double toleranceMetres)
    {
        int uniqueCount = closedRing.Count - 1;
        if (uniqueCount < 3 || closedRing[0] != closedRing[^1])
            throw new InvalidOperationException("A Geo polygon ring is not a valid explicitly closed ring.");

        GeoPoint anchor = closedRing[0];
        int farthestIndex = 1;
        double farthestDistance = -1d;
        for (int i = 1; i < uniqueCount; i++)
        {
            double distance = GeoEncodingUtils.HaversineDistance(
                anchor.Latitude,
                anchor.Longitude,
                closedRing[i].Latitude,
                closedRing[i].Longitude);
            if (distance > farthestDistance)
            {
                farthestDistance = distance;
                farthestIndex = i;
            }
        }

        GeoPoint[] firstArc = closedRing.Take(farthestIndex + 1).ToArray();
        var secondArc = new List<GeoPoint>(uniqueCount - farthestIndex + 1);
        for (int i = farthestIndex; i < uniqueCount; i++)
            secondArc.Add(closedRing[i]);
        secondArc.Add(anchor);

        GeoPoint[] simplifiedFirst = SimplifyPath(firstArc, toleranceMetres);
        GeoPoint[] simplifiedSecond = SimplifyPath(secondArc.ToArray(), toleranceMetres);
        var result = new List<GeoPoint>(simplifiedFirst.Length + simplifiedSecond.Length - 1);
        result.AddRange(simplifiedFirst);
        for (int i = 1; i < simplifiedSecond.Length; i++)
            result.Add(simplifiedSecond[i]);

        if (result.Count < 2 || result[0] != result[^1])
            result.Add(result[0]);
        if (result.Take(result.Count - 1).Distinct().Count() < 3)
            throw new InvalidOperationException("Geo polygon simplification would leave fewer than three distinct ring vertices.");
        return result.ToArray();
    }

    private static GeoPoint[] SimplifyPath(GeoPoint[] points, double toleranceMetres)
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
                double distance = DistanceToMinorArc(points[i], points[start], points[end]);
                if (distance > maximumDistance)
                {
                    maximumDistance = distance;
                    selectedIndex = i;
                }
            }

            if (selectedIndex < 0 || maximumDistance <= toleranceMetres)
                continue;

            keep[selectedIndex] = true;
            pending.Push((selectedIndex, end));
            pending.Push((start, selectedIndex));
        }

        var simplified = new List<GeoPoint>();
        for (int i = 0; i < points.Length; i++)
            if (keep[i])
                simplified.Add(points[i]);
        return simplified.ToArray();
    }

    private static double DistanceToMinorArc(GeoPoint point, GeoPoint start, GeoPoint end)
    {
        Vector3 p = ToUnitVector(point);
        Vector3 a = ToUnitVector(start);
        Vector3 b = ToUnitVector(end);
        Vector3 normal = Vector3.Cross(a, b);
        double normalLength = normal.Length;
        if (!double.IsFinite(normalLength) || normalLength <= 1e-15d)
            return Math.Min(Distance(point, start), Distance(point, end));

        normal = normal / normalLength;
        Vector3 projection = p - (normal * Vector3.Dot(p, normal));
        double projectionLength = projection.Length;
        if (!double.IsFinite(projectionLength) || projectionLength <= 1e-15d)
            return Math.Min(Distance(point, start), Distance(point, end));

        Vector3 closest = projection / projectionLength;
        if (Vector3.Dot(closest, p) < 0d)
            closest = closest * -1d;

        double segmentAngle = AngularDistance(a, b);
        double projectedAngle = AngularDistance(a, closest) + AngularDistance(closest, b);
        if (!double.IsFinite(projectedAngle) || projectedAngle > segmentAngle + 1e-12d)
            return Math.Min(Distance(point, start), Distance(point, end));

        return AngularDistance(p, closest) * EarthRadiusMetres;
    }

    private static double Distance(GeoPoint left, GeoPoint right)
        => GeoEncodingUtils.HaversineDistance(left.Latitude, left.Longitude, right.Latitude, right.Longitude);

    private static double AngularDistance(Vector3 left, Vector3 right)
        => Math.Atan2(Vector3.Cross(left, right).Length, Math.Clamp(Vector3.Dot(left, right), -1d, 1d));

    private static Vector3 ToUnitVector(GeoPoint point)
    {
        double latitude = point.Latitude * (Math.PI / 180d);
        double longitude = point.Longitude * (Math.PI / 180d);
        double latitudeCosine = Math.Cos(latitude);
        return new Vector3(
            latitudeCosine * Math.Cos(longitude),
            latitudeCosine * Math.Sin(longitude),
            Math.Sin(latitude));
    }

    private readonly record struct Vector3(double X, double Y, double Z)
    {
        internal double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));
        internal static double Dot(Vector3 left, Vector3 right)
            => (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
        internal static Vector3 Cross(Vector3 left, Vector3 right)
            => new(
                (left.Y * right.Z) - (left.Z * right.Y),
                (left.Z * right.X) - (left.X * right.Z),
                (left.X * right.Y) - (left.Y * right.X));
        public static Vector3 operator +(Vector3 left, Vector3 right)
            => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        public static Vector3 operator -(Vector3 left, Vector3 right)
            => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        public static Vector3 operator *(Vector3 value, double scalar)
            => new(value.X * scalar, value.Y * scalar, value.Z * scalar);
        public static Vector3 operator /(Vector3 value, double scalar)
            => new(value.X / scalar, value.Y / scalar, value.Z / scalar);
    }
}


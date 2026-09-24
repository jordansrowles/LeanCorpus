using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;

/// <summary>Exact predicates over quantised point, line and triangle primitives.</summary>
internal static class SpatialGeometryRelations
{
    private const double CoverageTolerance = 1e-10;

    internal static bool ContainsPoint(ShapePrimitive primitive, ShapeVertex point)
    {
        if (IsGeo(primitive.A))
            point = Shift(point, 360d * Math.Round((PrimitiveCentreX(primitive) - point.X) / 360d));
        if (primitive.Kind == ShapePrimitiveKind.Point)
            return Same(primitive.A, point);
        if (primitive.Kind == ShapePrimitiveKind.Line)
            return OnSegment(primitive.A, primitive.B, point);
        return PointInTriangle(point.X, point.Y, primitive.A, primitive.B, primitive.C);
    }

    internal static bool ContainsPoint(IReadOnlyList<ShapePrimitive> primitives, ShapeVertex point)
    {
        foreach (ShapePrimitive primitive in primitives)
            if (ContainsPoint(primitive, point))
                return true;
        return false;
    }

    internal static bool ContainsPoint(GeoCircle circle, ShapeVertex point)
        => GeoEncodingUtils.HaversineDistance(circle.Latitude, circle.Longitude, point.Y, point.X) <= circle.RadiusMetres;

    internal static bool ContainsPoint(XYCircle circle, ShapeVertex point)
    {
        double dx = point.X - circle.X;
        double dy = point.Y - circle.Y;
        return dx * dx + dy * dy <= (double)circle.Radius * circle.Radius;
    }

    internal static bool Intersects(ShapePrimitive first, ShapePrimitive second, bool isGeo)
    {
        if (isGeo)
        {
            double alignment = 360d * Math.Round((PrimitiveCentreX(first) - PrimitiveCentreX(second)) / 360d);
            for (int offset = -1; offset <= 1; offset++)
                if (IntersectsUnwrapped(first, Shift(second, alignment + (offset * 360d))))
                    return true;
            return false;
        }
        return IntersectsUnwrapped(first, second);
    }

    private static bool IntersectsUnwrapped(ShapePrimitive first, ShapePrimitive second)
    {
        if (first.Kind == ShapePrimitiveKind.Point)
            return ContainsPoint(second, first.A);
        if (second.Kind == ShapePrimitiveKind.Point)
            return ContainsPoint(first, second.A);

        int firstEdges = EdgeCount(first);
        int secondEdges = EdgeCount(second);
        for (int i = 0; i < firstEdges; i++)
        {
            GetEdge(first, i, out ShapeVertex a, out ShapeVertex b);
            for (int j = 0; j < secondEdges; j++)
            {
                GetEdge(second, j, out ShapeVertex c, out ShapeVertex d);
                if (SegmentsIntersect(a, b, c, d))
                    return true;
            }
        }

        if (first.Kind == ShapePrimitiveKind.Triangle && ContainsPoint(second, first.A))
            return true;
        if (second.Kind == ShapePrimitiveKind.Triangle && ContainsPoint(first, second.A))
            return true;
        if (first.Kind == ShapePrimitiveKind.Line && second.Kind == ShapePrimitiveKind.Triangle)
            return ContainsPoint(second, first.A) || ContainsPoint(second, first.B);
        if (second.Kind == ShapePrimitiveKind.Line && first.Kind == ShapePrimitiveKind.Triangle)
            return ContainsPoint(first, second.A) || ContainsPoint(first, second.B);
        return false;
    }

    internal static bool IntersectsCircle(ShapePrimitive primitive, XYCircle circle)
    {
        double radiusSquared = (double)circle.Radius * circle.Radius;
        if (primitive.Kind == ShapePrimitiveKind.Point)
            return ContainsPoint(circle, primitive.A);
        if (primitive.Kind == ShapePrimitiveKind.Triangle
            && PointInTriangle(circle.X, circle.Y, primitive.A, primitive.B, primitive.C))
            return true;

        for (int i = 0; i < EdgeCount(primitive); i++)
        {
            GetEdge(primitive, i, out ShapeVertex a, out ShapeVertex b);
            if (DistanceSquaredToSegment(circle.X, circle.Y, a.X, a.Y, b.X, b.Y) <= radiusSquared)
                return true;
        }
        return false;
    }

    internal static bool IntersectsCircle(ShapePrimitive primitive, GeoCircle circle)
    {
        if (circle.RadiusMetres >= Math.PI * 6_371_000d)
            return true;
        if (primitive.Kind == ShapePrimitiveKind.Point)
            return ContainsPoint(circle, primitive.A);

        double centreLongitude = UnwrapNear((primitive.A.X + primitive.B.X + primitive.C.X) / 3, circle.Longitude);
        if (primitive.Kind == ShapePrimitiveKind.Triangle
            && PointInTriangle(centreLongitude, circle.Latitude, primitive.A, primitive.B, primitive.C))
            return true;

        for (int i = 0; i < EdgeCount(primitive); i++)
        {
            GetEdge(primitive, i, out ShapeVertex a, out ShapeVertex b);
            if (MinimumGeoDistanceToSegment(circle, a, b) <= circle.RadiusMetres)
                return true;
        }
        return false;
    }

    internal static bool LineCoveredByQuery(ShapeVertex a, ShapeVertex b, PreparedShapeQuery query)
    {
        var intervals = new List<ParameterInterval>();
        foreach (ShapePrimitive primitive in query.Primitives)
            AddCoverageInterval(a, b, query.IsGeo ? Align(primitive, (a.X + b.X) / 2) : primitive, intervals);
        foreach (XYCircle circle in query.XYCircles)
            AddCircleInterval(a, b, circle, intervals);
        foreach (GeoCircle circle in query.GeoCircles)
            AddGeoCircleCoverageIntervals(a, b, circle, intervals);
        return IntervalsCoverUnit(intervals);
    }

    internal static bool LineCoveredByPrimitives(
        ShapeVertex a,
        ShapeVertex b,
        IReadOnlyList<ShapePrimitive> primitives,
        bool isGeo)
    {
        var intervals = new List<ParameterInterval>();
        foreach (ShapePrimitive primitive in primitives)
            AddCoverageInterval(a, b, isGeo ? Align(primitive, (a.X + b.X) / 2) : primitive, intervals);
        return IntervalsCoverUnit(intervals);
    }

    internal static bool TriangleCoveredByQuery(ShapePrimitive triangle, PreparedShapeQuery query)
    {
        List<List<ShapeVertex>> remaining = UncoveredTriangleParts(triangle, query.Primitives, query.IsGeo);
        if (remaining.Count == 0)
            return true;
        if (query.IsGeo)
            return query.GeoCircles.Count > 0
                && remaining.All(polygon => GeoCircleUnionCoversPolygon(polygon, query.GeoCircles));
        return query.XYCircles.Count > 0
            && remaining.All(polygon => XYCircleUnionCoversPolygon(polygon, query.XYCircles));
    }

    internal static bool TriangleCoveredByPrimitives(
        ShapePrimitive triangle,
        IReadOnlyList<ShapePrimitive> primitives,
        bool isGeo)
    {
        return UncoveredTriangleParts(triangle, primitives, isGeo).Count == 0;
    }

    private static List<List<ShapeVertex>> UncoveredTriangleParts(
        ShapePrimitive triangle,
        IReadOnlyList<ShapePrimitive> primitives,
        bool isGeo)
    {
        double targetArea = Math.Abs(Area(triangle.A, triangle.B, triangle.C));
        if (targetArea == 0)
            return ContainsPoint(primitives, triangle.A)
                ? []
                : [[triangle.A, triangle.B, triangle.C]];

        var remaining = new List<List<ShapeVertex>>(1)
        {
            new() { triangle.A, triangle.B, triangle.C },
        };
        foreach (ShapePrimitive cover in primitives)
        {
            if (cover.Kind != ShapePrimitiveKind.Triangle)
                continue;
            ShapePrimitive alignedCover = isGeo ? Align(cover, PrimitiveCentreX(triangle)) : cover;
            double minX = Math.Min(alignedCover.A.X, Math.Min(alignedCover.B.X, alignedCover.C.X));
            double maxX = Math.Max(alignedCover.A.X, Math.Max(alignedCover.B.X, alignedCover.C.X));
            double targetMinX = Math.Min(triangle.A.X, Math.Min(triangle.B.X, triangle.C.X));
            double targetMaxX = Math.Max(triangle.A.X, Math.Max(triangle.B.X, triangle.C.X));
            if (maxX < targetMinX || minX > targetMaxX)
                continue;

            var uncovered = new List<List<ShapeVertex>>();
            foreach (List<ShapeVertex> polygon in remaining)
                SubtractTriangle(polygon, alignedCover, uncovered);
            remaining = uncovered;
            if (remaining.Count == 0)
                return remaining;
        }

        double tolerance = Math.Max(CoverageTolerance, targetArea * CoverageTolerance);
        remaining.RemoveAll(polygon => PolygonArea(polygon) <= tolerance);
        return remaining;
    }

    private static bool XYCircleUnionCoversPolygon(
        IReadOnlyList<ShapeVertex> polygon,
        IReadOnlyList<XYCircle> circles)
    {
        XYCircle[] uniqueCircles = circles.Distinct().ToArray();
        for (int circleIndex = 0; circleIndex < uniqueCircles.Length; circleIndex++)
        {
            XYCircle circle = uniqueCircles[circleIndex];
            var powerCell = new List<ShapeVertex>(polygon);
            double circleRadiusSquared = (double)circle.Radius * circle.Radius;
            double circleConstant = circleRadiusSquared - ((double)circle.X * circle.X) - ((double)circle.Y * circle.Y);

            for (int otherIndex = 0; otherIndex < uniqueCircles.Length && powerCell.Count >= 3; otherIndex++)
            {
                if (otherIndex == circleIndex)
                    continue;

                XYCircle other = uniqueCircles[otherIndex];
                double otherRadiusSquared = (double)other.Radius * other.Radius;
                double otherConstant = otherRadiusSquared - ((double)other.X * other.X) - ((double)other.Y * other.Y);
                powerCell = ClipPowerHalfPlane(
                    powerCell,
                    2d * (circle.X - other.X),
                    2d * (circle.Y - other.Y),
                    circleConstant - otherConstant);
            }

            if (powerCell.Count < 3)
                continue;

            foreach (ShapeVertex vertex in powerCell)
            {
                double dx = vertex.X - circle.X;
                double dy = vertex.Y - circle.Y;
                if (dx * dx + dy * dy > circleRadiusSquared)
                    return false;
            }
        }

        return uniqueCircles.Length > 0;
    }

    private static bool GeoCircleUnionCoversPolygon(
        IReadOnlyList<ShapeVertex> polygon,
        IReadOnlyList<GeoCircle> circles)
    {
        const double earthRadiusMetres = 6_371_000d;
        const double radiansToDegrees = 180d / Math.PI;

        if (circles.Any(static circle => circle.RadiusMetres >= Math.PI * 6_371_000d))
            return true;

        var relevantCircles = circles
            .Distinct()
            .Where(circle => GeoCircleEnvelopeIntersectsPolygon(circle, polygon))
            .ToArray();
        if (relevantCircles.Length == 0)
            return false;

        var criticalLatitudes = new List<double>(polygon.Count + relevantCircles.Length * 4);
        foreach (ShapeVertex vertex in polygon)
            criticalLatitudes.Add(vertex.Y);

        for (int circleIndex = 0; circleIndex < relevantCircles.Length; circleIndex++)
        {
            GeoCircle circle = relevantCircles[circleIndex];
            double angularRadius = circle.RadiusMetres / earthRadiusMetres;
            double angularRadiusDegrees = angularRadius * radiansToDegrees;
            criticalLatitudes.Add(Math.Clamp(circle.Latitude - angularRadiusDegrees, -90, 90));
            criticalLatitudes.Add(Math.Clamp(circle.Latitude + angularRadiusDegrees, -90, 90));
            double oppositeLongitudeBoundary = (Math.PI - angularRadius) * radiansToDegrees;
            AddCriticalLatitude(oppositeLongitudeBoundary - circle.Latitude);
            AddCriticalLatitude(-oppositeLongitudeBoundary - circle.Latitude);

            for (int edgeIndex = 0; edgeIndex < polygon.Count; edgeIndex++)
                AddGeoCircleEdgeIntersectionLatitudes(
                    circle,
                    polygon[edgeIndex],
                    polygon[(edgeIndex + 1) % polygon.Count],
                    criticalLatitudes);

            for (int otherIndex = circleIndex + 1; otherIndex < relevantCircles.Length; otherIndex++)
                AddGeoCircleIntersectionLatitudes(circle, relevantCircles[otherIndex], criticalLatitudes);

            void AddCriticalLatitude(double latitude)
            {
                if (latitude is >= -90 and <= 90)
                    criticalLatitudes.Add(latitude);
            }
        }

        criticalLatitudes.Sort();
        var uniqueLatitudes = new List<double>(criticalLatitudes.Count);
        foreach (double latitude in criticalLatitudes)
            if (uniqueLatitudes.Count == 0 || Math.Abs(latitude - uniqueLatitudes[^1]) > 1e-10)
                uniqueLatitudes.Add(latitude);

        for (int i = 0; i < uniqueLatitudes.Count; i++)
        {
            if (!GeoCircleUnionCoversLatitude(polygon, relevantCircles, uniqueLatitudes[i]))
                return false;
            if (i + 1 < uniqueLatitudes.Count)
            {
                double middle = (uniqueLatitudes[i] + uniqueLatitudes[i + 1]) / 2;
                if (middle > uniqueLatitudes[i]
                    && middle < uniqueLatitudes[i + 1]
                    && !GeoCircleUnionCoversLatitude(polygon, relevantCircles, middle))
                    return false;
            }
        }
        return uniqueLatitudes.Count > 0;
    }

    private static bool GeoCircleEnvelopeIntersectsPolygon(
        GeoCircle circle,
        IReadOnlyList<ShapeVertex> polygon)
    {
        const double earthRadiusMetres = 6_371_000d;
        const double radiansToDegrees = 180d / Math.PI;
        const double degreesToRadians = Math.PI / 180d;

        double minX = polygon.Min(static vertex => vertex.X);
        double maxX = polygon.Max(static vertex => vertex.X);
        double minY = polygon.Min(static vertex => vertex.Y);
        double maxY = polygon.Max(static vertex => vertex.Y);
        double angularRadius = circle.RadiusMetres / earthRadiusMetres;
        double angularRadiusDegrees = angularRadius * radiansToDegrees;
        if (circle.Latitude + angularRadiusDegrees < minY
            || circle.Latitude - angularRadiusDegrees > maxY)
            return false;

        if (circle.Latitude + angularRadiusDegrees >= 90
            || circle.Latitude - angularRadiusDegrees <= -90)
            return true;

        double longitudeRatio = Math.Sin(angularRadius) / Math.Cos(circle.Latitude * degreesToRadians);
        double longitudeRadius = Math.Asin(Math.Clamp(longitudeRatio, -1, 1)) * radiansToDegrees;
        for (int world = -1; world <= 1; world++)
        {
            double centre = circle.Longitude + (world * 360d);
            if (centre + longitudeRadius >= minX && centre - longitudeRadius <= maxX)
                return true;
        }
        return false;
    }

    private static bool GeoCircleUnionCoversLatitude(
        IReadOnlyList<ShapeVertex> polygon,
        IReadOnlyList<GeoCircle> circles,
        double latitude)
    {
        if (!TryGetHorizontalSpan(polygon, latitude, out double minimumX, out double maximumX))
            return true;

        var intervals = new List<ParameterInterval>(circles.Count * 2);
        foreach (GeoCircle circle in circles)
            AddGeoCircleLongitudeIntervals(circle, latitude, minimumX, maximumX, intervals);
        if (intervals.Count == 0)
            return false;

        intervals.Sort(static (first, second) => first.Minimum.CompareTo(second.Minimum));
        double coveredUntil = minimumX;
        foreach (ParameterInterval interval in intervals)
        {
            if (interval.Maximum < minimumX || interval.Minimum > maximumX)
                continue;
            if (interval.Minimum > coveredUntil + CoverageTolerance)
                return false;
            coveredUntil = Math.Max(coveredUntil, interval.Maximum);
            if (coveredUntil >= maximumX - CoverageTolerance)
                return true;
        }
        return false;
    }

    private static void AddGeoCircleLongitudeIntervals(
        GeoCircle circle,
        double latitude,
        double minimumX,
        double maximumX,
        List<ParameterInterval> intervals)
    {
        const double earthRadiusMetres = 6_371_000d;
        const double degreesToRadians = Math.PI / 180d;
        const double radiansToDegrees = 180d / Math.PI;

        if (circle.RadiusMetres >= Math.PI * earthRadiusMetres)
        {
            intervals.Add(new ParameterInterval(minimumX, maximumX));
            return;
        }

        double latitudeRadians = latitude * degreesToRadians;
        double centreLatitudeRadians = circle.Latitude * degreesToRadians;
        double denominator = Math.Cos(latitudeRadians) * Math.Cos(centreLatitudeRadians);
        if (Math.Abs(latitude) == 90 || Math.Abs(circle.Latitude) == 90)
        {
            if (GeoEncodingUtils.HaversineDistance(circle.Latitude, circle.Longitude, latitude, circle.Longitude)
                <= circle.RadiusMetres)
                intervals.Add(new ParameterInterval(minimumX, maximumX));
            return;
        }

        double cosineLongitude = (Math.Cos(circle.RadiusMetres / earthRadiusMetres)
            - (Math.Sin(latitudeRadians) * Math.Sin(centreLatitudeRadians))) / denominator;
        if (cosineLongitude > 1)
            return;
        if (cosineLongitude <= -1)
        {
            intervals.Add(new ParameterInterval(minimumX, maximumX));
            return;
        }

        double halfWidth = Math.Acos(cosineLongitude) * radiansToDegrees;
        for (int world = -2; world <= 2; world++)
        {
            double centre = circle.Longitude + (world * 360d);
            double intervalMinimum = Math.Max(minimumX, centre - halfWidth);
            double intervalMaximum = Math.Min(maximumX, centre + halfWidth);
            if (intervalMinimum <= intervalMaximum)
                intervals.Add(new ParameterInterval(intervalMinimum, intervalMaximum));
        }
    }

    private static bool TryGetHorizontalSpan(
        IReadOnlyList<ShapeVertex> polygon,
        double latitude,
        out double minimumX,
        out double maximumX)
    {
        minimumX = double.PositiveInfinity;
        maximumX = double.NegativeInfinity;
        for (int i = 0; i < polygon.Count; i++)
        {
            ShapeVertex a = polygon[i];
            ShapeVertex b = polygon[(i + 1) % polygon.Count];
            double low = Math.Min(a.Y, b.Y);
            double high = Math.Max(a.Y, b.Y);
            if (latitude < low || latitude > high)
                continue;

            if (a.Y == b.Y)
            {
                if (latitude == a.Y)
                {
                    minimumX = Math.Min(minimumX, Math.Min(a.X, b.X));
                    maximumX = Math.Max(maximumX, Math.Max(a.X, b.X));
                }
                continue;
            }

            double fraction = (latitude - a.Y) / (b.Y - a.Y);
            double x = a.X + (fraction * (b.X - a.X));
            minimumX = Math.Min(minimumX, x);
            maximumX = Math.Max(maximumX, x);
        }

        return double.IsFinite(minimumX) && double.IsFinite(maximumX);
    }

    private static void AddGeoCircleEdgeIntersectionLatitudes(
        GeoCircle circle,
        ShapeVertex start,
        ShapeVertex end,
        List<double> criticalLatitudes)
    {
        const double degreesToRadians = Math.PI / 180d;
        const int derivativeSteps = 64;

        double longitudeStart = start.X * degreesToRadians;
        double latitudeStart = start.Y * degreesToRadians;
        double longitudeDelta = (end.X - start.X) * degreesToRadians;
        double latitudeDelta = (end.Y - start.Y) * degreesToRadians;
        double centreLongitude = circle.Longitude * degreesToRadians;
        double centreLatitude = circle.Latitude * degreesToRadians;
        double boundaryCosine = Math.Cos(circle.RadiusMetres / 6_371_000d);

        double Boundary(double t)
        {
            double latitude = latitudeStart + (latitudeDelta * t);
            double longitudeDifference = longitudeStart + (longitudeDelta * t) - centreLongitude;
            return (Math.Sin(latitude) * Math.Sin(centreLatitude))
                + (Math.Cos(latitude) * Math.Cos(centreLatitude) * Math.Cos(longitudeDifference))
                - boundaryCosine;
        }

        double Derivative(double t)
        {
            double latitude = latitudeStart + (latitudeDelta * t);
            double longitudeDifference = longitudeStart + (longitudeDelta * t) - centreLongitude;
            return (latitudeDelta * Math.Cos(latitude) * Math.Sin(centreLatitude))
                - (latitudeDelta * Math.Sin(latitude) * Math.Cos(centreLatitude) * Math.Cos(longitudeDifference))
                - (longitudeDelta * Math.Cos(latitude) * Math.Cos(centreLatitude) * Math.Sin(longitudeDifference));
        }

        var extrema = new List<double> { 0, 1 };
        double previousT = 0;
        double previousDerivative = Derivative(previousT);
        for (int step = 1; step <= derivativeSteps; step++)
        {
            double currentT = (double)step / derivativeSteps;
            double currentDerivative = Derivative(currentT);
            if ((previousDerivative < 0 && currentDerivative > 0)
                || (previousDerivative > 0 && currentDerivative < 0))
            {
                double low = previousT;
                double high = currentT;
                bool lowPositive = previousDerivative > 0;
                for (int iteration = 0; iteration < 56; iteration++)
                {
                    double middle = (low + high) / 2;
                    bool middlePositive = Derivative(middle) > 0;
                    if (middlePositive == lowPositive)
                        low = middle;
                    else
                        high = middle;
                }
                extrema.Add((low + high) / 2);
            }
            previousT = currentT;
            previousDerivative = currentDerivative;
        }
        extrema.Sort();

        for (int i = 0; i < extrema.Count; i++)
        {
            double t = extrema[i];
            double value = Boundary(t);
            if (Math.Abs(value) <= 1e-14)
                criticalLatitudes.Add(start.Y + ((end.Y - start.Y) * t));
            if (i + 1 >= extrema.Count)
                continue;

            double nextT = extrema[i + 1];
            double nextValue = Boundary(nextT);
            if ((value < 0 && nextValue > 0) || (value > 0 && nextValue < 0))
            {
                double low = t;
                double high = nextT;
                bool lowPositive = value > 0;
                for (int iteration = 0; iteration < 56; iteration++)
                {
                    double middle = (low + high) / 2;
                    bool middlePositive = Boundary(middle) > 0;
                    if (middlePositive == lowPositive)
                        low = middle;
                    else
                        high = middle;
                }
                double intersection = (low + high) / 2;
                criticalLatitudes.Add(start.Y + ((end.Y - start.Y) * intersection));
            }
        }
    }

    private static void AddGeoCircleIntersectionLatitudes(
        GeoCircle first,
        GeoCircle second,
        List<double> criticalLatitudes)
    {
        const double radiansToDegrees = 180d / Math.PI;
        const double earthRadiusMetres = 6_371_000d;
        if (first.RadiusMetres >= Math.PI * earthRadiusMetres
            || second.RadiusMetres >= Math.PI * earthRadiusMetres)
            return;

        SpatialVector3 firstCentre = GeoUnitVector(first.Latitude, first.Longitude);
        SpatialVector3 secondCentre = GeoUnitVector(second.Latitude, second.Longitude);
        double dot = SpatialVector3.Dot(firstCentre, secondCentre);
        double determinant = 1 - (dot * dot);
        if (determinant <= 1e-14)
            return;

        double firstBoundary = Math.Cos(first.RadiusMetres / earthRadiusMetres);
        double secondBoundary = Math.Cos(second.RadiusMetres / earthRadiusMetres);
        double firstCoefficient = (firstBoundary - (dot * secondBoundary)) / determinant;
        double secondCoefficient = (secondBoundary - (dot * firstBoundary)) / determinant;
        SpatialVector3 point = SpatialVector3.Add(
            SpatialVector3.Scale(firstCentre, firstCoefficient),
            SpatialVector3.Scale(secondCentre, secondCoefficient));
        SpatialVector3 direction = SpatialVector3.Cross(firstCentre, secondCentre);
        double directionSquared = SpatialVector3.Dot(direction, direction);
        double remaining = 1 - SpatialVector3.Dot(point, point);
        if (remaining < -1e-12)
            return;

        double scale = Math.Sqrt(Math.Max(0, remaining) / directionSquared);
        AddLatitude(SpatialVector3.Add(point, SpatialVector3.Scale(direction, scale)));
        AddLatitude(SpatialVector3.Add(point, SpatialVector3.Scale(direction, -scale)));

        void AddLatitude(SpatialVector3 value)
        {
            double latitude = Math.Asin(Math.Clamp(value.Z, -1, 1)) * radiansToDegrees;
            criticalLatitudes.Add(latitude);
        }
    }

    private static SpatialVector3 GeoUnitVector(double latitude, double longitude)
    {
        const double degreesToRadians = Math.PI / 180d;
        double latitudeRadians = latitude * degreesToRadians;
        double longitudeRadians = longitude * degreesToRadians;
        double cosineLatitude = Math.Cos(latitudeRadians);
        return new SpatialVector3(
            cosineLatitude * Math.Cos(longitudeRadians),
            cosineLatitude * Math.Sin(longitudeRadians),
            Math.Sin(latitudeRadians));
    }

    private readonly record struct SpatialVector3(double X, double Y, double Z)
    {
        internal static double Dot(SpatialVector3 first, SpatialVector3 second)
            => (first.X * second.X) + (first.Y * second.Y) + (first.Z * second.Z);

        internal static SpatialVector3 Add(SpatialVector3 first, SpatialVector3 second)
            => new(first.X + second.X, first.Y + second.Y, first.Z + second.Z);

        internal static SpatialVector3 Scale(SpatialVector3 value, double scale)
            => new(value.X * scale, value.Y * scale, value.Z * scale);

        internal static SpatialVector3 Cross(SpatialVector3 first, SpatialVector3 second)
            => new(
                (first.Y * second.Z) - (first.Z * second.Y),
                (first.Z * second.X) - (first.X * second.Z),
                (first.X * second.Y) - (first.Y * second.X));
    }

    private static List<ShapeVertex> ClipPowerHalfPlane(
        List<ShapeVertex> polygon,
        double xCoefficient,
        double yCoefficient,
        double constant)
    {
        if (polygon.Count == 0)
            return polygon;

        var clipped = new List<ShapeVertex>(polygon.Count + 1);
        ShapeVertex previous = polygon[^1];
        double previousValue = (xCoefficient * previous.X) + (yCoefficient * previous.Y) + constant;
        bool previousInside = previousValue >= 0;
        foreach (ShapeVertex current in polygon)
        {
            double currentValue = (xCoefficient * current.X) + (yCoefficient * current.Y) + constant;
            bool currentInside = currentValue >= 0;
            if (currentInside != previousInside)
            {
                double fraction = previousValue / (previousValue - currentValue);
                clipped.Add(new ShapeVertex(
                    previous.X + (fraction * (current.X - previous.X)),
                    previous.Y + (fraction * (current.Y - previous.Y))));
            }
            if (currentInside)
                clipped.Add(current);
            previous = current;
            previousValue = currentValue;
            previousInside = currentInside;
        }
        return clipped;
    }

    internal static bool XYCircleCoveredByPrimitives(XYCircle circle, IReadOnlyList<ShapePrimitive> primitives)
    {
        ShapeVertex centre = ShapePrimitiveCodec.Quantise(
            new ShapeVertex(circle.X, circle.Y), SpatialFieldKind.XYShape);
        if (!ContainsPoint(primitives, centre))
            return false;
        if (circle.Radius == 0)
            return true;

        bool hasSurface = false;
        double radiusSquared = (double)circle.Radius * circle.Radius;
        foreach (ShapePrimitive primitive in primitives)
        {
            if (primitive.Kind != ShapePrimitiveKind.Triangle)
                continue;
            hasSurface = true;
            for (int i = 0; i < 3; i++)
            {
                if (!IsSourceEdge(primitive, i))
                    continue;
                GetEdge(primitive, i, out ShapeVertex a, out ShapeVertex b);
                if (DistanceSquaredToSegment(circle.X, circle.Y, a.X, a.Y, b.X, b.Y) < radiusSquared)
                    return false;
            }
        }
        return hasSurface;
    }

    internal static bool GeoCircleCoveredByPrimitives(GeoCircle circle, IReadOnlyList<ShapePrimitive> primitives)
    {
        ShapeVertex centre = ShapePrimitiveCodec.Quantise(
            new ShapeVertex(circle.Longitude, circle.Latitude), SpatialFieldKind.GeoShape);
        if (!ContainsPoint(primitives, centre))
            return false;
        if (circle.RadiusMetres == 0)
            return true;
        if (circle.RadiusMetres >= Math.PI * 6_371_000d)
            return false;

        bool hasSurface = false;
        foreach (ShapePrimitive primitive in primitives)
        {
            if (primitive.Kind != ShapePrimitiveKind.Triangle)
                continue;
            hasSurface = true;
            for (int i = 0; i < 3; i++)
            {
                if (!IsSourceEdge(primitive, i))
                    continue;
                GetEdge(primitive, i, out ShapeVertex a, out ShapeVertex b);
                if (MinimumGeoDistanceToSegment(circle, a, b) < circle.RadiusMetres)
                    return false;
            }
        }
        return hasSurface;
    }

    private static void SubtractTriangle(
        List<ShapeVertex> subject,
        ShapePrimitive clip,
        List<List<ShapeVertex>> uncovered)
    {
        var inside = subject;
        ShapeVertex[] clipVertices = [clip.A, clip.B, clip.C];
        for (int edge = 0; edge < 3 && inside.Count >= 3; edge++)
        {
            ShapeVertex a = clipVertices[edge];
            ShapeVertex b = clipVertices[(edge + 1) % 3];
            List<ShapeVertex> outsidePart = ClipHalfPlane(inside, a, b, keepPositive: false);
            if (outsidePart.Count >= 3 && PolygonArea(outsidePart) > 0)
                uncovered.Add(outsidePart);
            inside = ClipHalfPlane(inside, a, b, keepPositive: true);
        }
        // Any remaining part is inside all three half-planes and is covered by the clip triangle.
    }

    private static List<ShapeVertex> ClipHalfPlane(
        List<ShapeVertex> polygon,
        ShapeVertex a,
        ShapeVertex b,
        bool keepPositive)
    {
        var output = new List<ShapeVertex>(polygon.Count + 1);
        ShapeVertex previous = polygon[^1];
        double previousSide = Cross(a, b, previous);
        bool previousInside = keepPositive ? previousSide >= 0 : previousSide <= 0;
        foreach (ShapeVertex current in polygon)
        {
            double currentSide = Cross(a, b, current);
            bool currentInside = keepPositive ? currentSide >= 0 : currentSide <= 0;
            if (currentInside != previousInside)
                output.Add(IntersectLines(previous, current, a, b));
            if (currentInside)
                output.Add(current);
            previous = current;
            previousSide = currentSide;
            previousInside = currentInside;
        }
        return output;
    }

    private static double PolygonArea(IReadOnlyList<ShapeVertex> polygon)
    {
        if (polygon.Count < 3)
            return 0;
        double area = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            ShapeVertex a = polygon[i];
            ShapeVertex b = polygon[(i + 1) % polygon.Count];
            area += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(area) / 2;
    }

    private static bool IsSourceEdge(ShapePrimitive primitive, int edge)
        => edge switch
        {
            0 => primitive.EdgeAB,
            1 => primitive.EdgeBC,
            _ => primitive.EdgeCA,
        };

    internal static bool ContainsPoint(GeoCircle circle, double latitude, double longitude)
        => GeoEncodingUtils.HaversineDistance(circle.Latitude, circle.Longitude, latitude, longitude) <= circle.RadiusMetres;

    private static void AddCoverageInterval(ShapeVertex a, ShapeVertex b, ShapePrimitive cover, List<ParameterInterval> intervals)
    {
        if (cover.Kind == ShapePrimitiveKind.Point)
        {
            if (Same(a, b) && Same(a, cover.A))
                intervals.Add(new ParameterInterval(0, 1));
            return;
        }
        if (cover.Kind == ShapePrimitiveKind.Line)
        {
            AddCollinearSegmentInterval(a, b, cover.A, cover.B, intervals);
            return;
        }

        double minimum = 0;
        double maximum = 1;
        ShapeVertex[] vertices = [cover.A, cover.B, cover.C];
        for (int i = 0; i < 3; i++)
        {
            ShapeVertex edgeStart = vertices[i];
            ShapeVertex edgeEnd = vertices[(i + 1) % 3];
            double startSide = Cross(edgeStart, edgeEnd, a);
            double endSide = Cross(edgeStart, edgeEnd, b);
            if (startSide < 0 && endSide < 0)
                return;
            if (startSide < 0)
                minimum = Math.Max(minimum, startSide / (startSide - endSide));
            else if (endSide < 0)
                maximum = Math.Min(maximum, startSide / (startSide - endSide));
            if (minimum > maximum)
                return;
        }
        intervals.Add(new ParameterInterval(Math.Clamp(minimum, 0, 1), Math.Clamp(maximum, 0, 1)));
    }

    private static void AddCircleInterval(ShapeVertex a, ShapeVertex b, XYCircle circle, List<ParameterInterval> intervals)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double ox = a.X - circle.X;
        double oy = a.Y - circle.Y;
        double aa = dx * dx + dy * dy;
        double bb = 2 * (ox * dx + oy * dy);
        double cc = ox * ox + oy * oy - (double)circle.Radius * circle.Radius;
        if (aa == 0)
        {
            if (cc <= 0)
                intervals.Add(new ParameterInterval(0, 1));
            return;
        }
        double discriminant = bb * bb - 4 * aa * cc;
        if (discriminant < 0)
            return;
        double root = Math.Sqrt(discriminant);
        double minimum = Math.Max(0, (-bb - root) / (2 * aa));
        double maximum = Math.Min(1, (-bb + root) / (2 * aa));
        if (minimum <= maximum)
            intervals.Add(new ParameterInterval(minimum, maximum));
    }

    private static void AddGeoCircleCoverageIntervals(ShapeVertex a, ShapeVertex b, GeoCircle circle, List<ParameterInterval> intervals)
    {
        const double degreesToRadians = Math.PI / 180d;
        const int derivativeSteps = 64;
        double longitudeStart = a.X * degreesToRadians;
        double latitudeStart = a.Y * degreesToRadians;
        double longitudeDelta = (b.X - a.X) * degreesToRadians;
        double latitudeDelta = (b.Y - a.Y) * degreesToRadians;
        double centreLongitude = circle.Longitude * degreesToRadians;
        double centreLatitude = circle.Latitude * degreesToRadians;

        double Derivative(double t)
        {
            double latitude = latitudeStart + (latitudeDelta * t);
            double longitudeDifference = longitudeStart + (longitudeDelta * t) - centreLongitude;
            return (latitudeDelta * Math.Cos(latitude) * Math.Sin(centreLatitude))
                - (latitudeDelta * Math.Sin(latitude) * Math.Cos(centreLatitude) * Math.Cos(longitudeDifference))
                - (longitudeDelta * Math.Cos(latitude) * Math.Cos(centreLatitude) * Math.Sin(longitudeDifference));
        }

        var criticalParameters = new List<double>(6) { 0, 1 };
        double previousT = 0;
        double previousDerivative = Derivative(previousT);
        for (int step = 1; step <= derivativeSteps; step++)
        {
            double currentT = (double)step / derivativeSteps;
            double currentDerivative = Derivative(currentT);
            if ((previousDerivative < 0 && currentDerivative > 0)
                || (previousDerivative > 0 && currentDerivative < 0))
            {
                double low = previousT;
                double high = currentT;
                bool lowPositive = previousDerivative > 0;
                for (int iteration = 0; iteration < 56; iteration++)
                {
                    double middle = (low + high) / 2;
                    if ((Derivative(middle) > 0) == lowPositive)
                        low = middle;
                    else
                        high = middle;
                }
                criticalParameters.Add((low + high) / 2);
            }
            else if (currentDerivative == 0 && currentT < 1)
                criticalParameters.Add(currentT);
            previousT = currentT;
            previousDerivative = currentDerivative;
        }

        criticalParameters.Sort();
        for (int i = criticalParameters.Count - 1; i > 0; i--)
            if (Math.Abs(criticalParameters[i] - criticalParameters[i - 1]) <= 1e-12)
                criticalParameters.RemoveAt(i);

        for (int i = 0; i < criticalParameters.Count; i++)
        {
            double start = criticalParameters[i];
            bool startInside = GeoDistanceAt(circle, a, b, start) <= circle.RadiusMetres;
            if (startInside)
                intervals.Add(new ParameterInterval(start, start));
            if (i + 1 >= criticalParameters.Count)
                continue;

            double end = criticalParameters[i + 1];
            bool endInside = GeoDistanceAt(circle, a, b, end) <= circle.RadiusMetres;
            if (startInside && endInside)
                intervals.Add(new ParameterInterval(start, end));
            else if (startInside != endInside)
            {
                double boundary = BisectGeoCircleBoundary(circle, a, b, start, end);
                intervals.Add(startInside
                    ? new ParameterInterval(start, boundary)
                    : new ParameterInterval(boundary, end));
            }
        }
    }

    private static bool IntervalsCoverUnit(List<ParameterInterval> intervals)
    {
        if (intervals.Count == 0)
            return false;
        intervals.Sort(static (first, second) => first.Minimum.CompareTo(second.Minimum));
        double coveredUntil = 0;
        foreach (ParameterInterval interval in intervals)
        {
            if (interval.Minimum > coveredUntil + CoverageTolerance)
                return false;
            coveredUntil = Math.Max(coveredUntil, interval.Maximum);
            if (coveredUntil >= 1 - CoverageTolerance)
                return true;
        }
        return false;
    }

    private static void AddCollinearSegmentInterval(ShapeVertex a, ShapeVertex b, ShapeVertex c, ShapeVertex d, List<ParameterInterval> intervals)
    {
        if (Same(a, b))
        {
            if (OnSegment(c, d, a))
                intervals.Add(new ParameterInterval(0, 1));
            return;
        }
        if (Cross(a, b, c) != 0 || Cross(a, b, d) != 0)
            return;
        double denominator = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
        double first = ((c.X - a.X) * (b.X - a.X) + (c.Y - a.Y) * (b.Y - a.Y)) / denominator;
        double second = ((d.X - a.X) * (b.X - a.X) + (d.Y - a.Y) * (b.Y - a.Y)) / denominator;
        double minimum = Math.Max(0, Math.Min(first, second));
        double maximum = Math.Min(1, Math.Max(first, second));
        if (minimum <= maximum)
            intervals.Add(new ParameterInterval(minimum, maximum));
    }

    private static double IntersectionArea(ShapePrimitive subject, ShapePrimitive clip)
    {
        var polygon = new List<ShapeVertex>(6) { subject.A, subject.B, subject.C };
        ShapeVertex[] clipVertices = [clip.A, clip.B, clip.C];
        for (int edge = 0; edge < 3 && polygon.Count > 0; edge++)
        {
            ShapeVertex a = clipVertices[edge];
            ShapeVertex b = clipVertices[(edge + 1) % 3];
            var output = new List<ShapeVertex>(polygon.Count + 1);
            ShapeVertex previous = polygon[^1];
            double previousSide = Cross(a, b, previous);
            foreach (ShapeVertex current in polygon)
            {
                double currentSide = Cross(a, b, current);
                if (currentSide >= 0)
                {
                    if (previousSide < 0)
                        output.Add(IntersectLines(previous, current, a, b));
                    output.Add(current);
                }
                else if (previousSide >= 0)
                    output.Add(IntersectLines(previous, current, a, b));
                previous = current;
                previousSide = currentSide;
            }
            polygon = output;
        }

        double area = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            ShapeVertex a = polygon[i];
            ShapeVertex b = polygon[(i + 1) % polygon.Count];
            area += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(area) / 2;
    }

    private static ShapeVertex IntersectLines(ShapeVertex a, ShapeVertex b, ShapeVertex c, ShapeVertex d)
    {
        double abX = b.X - a.X;
        double abY = b.Y - a.Y;
        double cdX = d.X - c.X;
        double cdY = d.Y - c.Y;
        double denominator = abX * cdY - abY * cdX;
        if (denominator == 0)
            return a;
        double t = ((c.X - a.X) * cdY - (c.Y - a.Y) * cdX) / denominator;
        return new ShapeVertex(a.X + (t * abX), a.Y + (t * abY));
    }

    private static double MinimumGeoDistanceToSegment(GeoCircle circle, ShapeVertex a, ShapeVertex b)
    {
        double low = 0;
        double high = 1;
        for (int i = 0; i < 72; i++)
        {
            double first = low + (high - low) / 3;
            double second = high - (high - low) / 3;
            if (GeoDistanceAt(circle, a, b, first) <= GeoDistanceAt(circle, a, b, second))
                high = second;
            else
                low = first;
        }
        return Math.Min(
            Math.Min(GeoDistanceAt(circle, a, b, 0), GeoDistanceAt(circle, a, b, 1)),
            GeoDistanceAt(circle, a, b, (low + high) / 2));
    }

    private static double GeoDistanceAt(GeoCircle circle, ShapeVertex a, ShapeVertex b, double t)
    {
        double longitude = a.X + (b.X - a.X) * t;
        double latitude = a.Y + (b.Y - a.Y) * t;
        return GeoEncodingUtils.HaversineDistance(circle.Latitude, circle.Longitude, latitude, longitude);
    }

    private static double BisectGeoCircleBoundary(GeoCircle circle, ShapeVertex a, ShapeVertex b, double low, double high)
    {
        bool lowInside = GeoDistanceAt(circle, a, b, low) <= circle.RadiusMetres;
        for (int i = 0; i < 56; i++)
        {
            double middle = (low + high) / 2;
            bool middleInside = GeoDistanceAt(circle, a, b, middle) <= circle.RadiusMetres;
            if (middleInside == lowInside)
                low = middle;
            else
                high = middle;
        }
        return (low + high) / 2;
    }

    private static double DistanceSquaredToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared == 0)
        {
            dx = px - ax;
            dy = py - ay;
            return dx * dx + dy * dy;
        }
        double t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1);
        dx = px - (ax + t * dx);
        dy = py - (ay + t * dy);
        return dx * dx + dy * dy;
    }

    private static bool PointInTriangle(double x, double y, ShapeVertex a, ShapeVertex b, ShapeVertex c)
        => Cross(a, b, x, y) >= 0 && Cross(b, c, x, y) >= 0 && Cross(c, a, x, y) >= 0;

    private static bool OnSegment(ShapeVertex a, ShapeVertex b, ShapeVertex point)
        => Cross(a, b, point) == 0
            && point.X >= Math.Min(a.X, b.X) && point.X <= Math.Max(a.X, b.X)
            && point.Y >= Math.Min(a.Y, b.Y) && point.Y <= Math.Max(a.Y, b.Y);

    private static bool Same(ShapeVertex a, ShapeVertex b)
    {
        if (a.HasPreparedKeys && b.HasPreparedKeys && a.PreparedKind == b.PreparedKind)
        {
            if (a.XKey == b.XKey && a.YKey == b.YKey)
                return true;
            return a.PreparedKind == SpatialFieldKind.GeoShape
                && a.YKey == b.YKey
                && Math.Abs(a.X) == 180
                && Math.Abs(b.X) == 180;
        }
        return a.X == b.X && a.Y == b.Y;
    }

    private static bool IsGeo(ShapeVertex vertex)
        => vertex.HasPreparedKeys && vertex.PreparedKind == SpatialFieldKind.GeoShape;

    private static ShapePrimitive Align(ShapePrimitive primitive, double referenceLongitude)
        => Shift(primitive, 360d * Math.Round((referenceLongitude - PrimitiveCentreX(primitive)) / 360d));

    private static ShapePrimitive Shift(ShapePrimitive primitive, double longitudeOffset)
        => longitudeOffset == 0
            ? primitive
            : new ShapePrimitive(
                Shift(primitive.A, longitudeOffset),
                Shift(primitive.B, longitudeOffset),
                Shift(primitive.C, longitudeOffset),
                primitive.EdgeAB,
                primitive.EdgeBC,
                primitive.EdgeCA,
                primitive.ValueOrdinal,
                primitive.Kind);

    private static ShapeVertex Shift(ShapeVertex vertex, double longitudeOffset)
        => longitudeOffset == 0
            ? vertex
            : vertex.HasPreparedKeys
                ? new ShapeVertex(vertex.X + longitudeOffset, vertex.Y, vertex.XKey, vertex.YKey, vertex.PreparedKind)
                : new ShapeVertex(vertex.X + longitudeOffset, vertex.Y);

    private static double PrimitiveCentreX(ShapePrimitive primitive)
        => primitive.Kind switch
        {
            ShapePrimitiveKind.Point => primitive.A.X,
            ShapePrimitiveKind.Line => (primitive.A.X + primitive.B.X) / 2,
            _ => (primitive.A.X + primitive.B.X + primitive.C.X) / 3,
        };

    private static double Cross(ShapeVertex a, ShapeVertex b, ShapeVertex c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static double Cross(ShapeVertex a, ShapeVertex b, double x, double y)
        => (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);

    private static bool SegmentsIntersect(ShapeVertex a, ShapeVertex b, ShapeVertex c, ShapeVertex d)
    {
        double abc = Cross(a, b, c);
        double abd = Cross(a, b, d);
        double cda = Cross(c, d, a);
        double cdb = Cross(c, d, b);
        if (abc == 0 && OnSegment(a, b, c)) return true;
        if (abd == 0 && OnSegment(a, b, d)) return true;
        if (cda == 0 && OnSegment(c, d, a)) return true;
        if (cdb == 0 && OnSegment(c, d, b)) return true;
        return (abc > 0) != (abd > 0) && (cda > 0) != (cdb > 0);
    }

    private static int EdgeCount(ShapePrimitive primitive)
        => primitive.Kind switch
        {
            ShapePrimitiveKind.Point => 0,
            ShapePrimitiveKind.Line => 1,
            _ => 3,
        };

    private static void GetEdge(ShapePrimitive primitive, int index, out ShapeVertex a, out ShapeVertex b)
    {
        if (primitive.Kind == ShapePrimitiveKind.Line)
        {
            a = primitive.A;
            b = primitive.B;
            return;
        }
        switch (index)
        {
            case 0: a = primitive.A; b = primitive.B; break;
            case 1: a = primitive.B; b = primitive.C; break;
            default: a = primitive.C; b = primitive.A; break;
        }
    }

    private static bool PointInTriangle(ShapeVertex point, ShapeVertex a, ShapeVertex b, ShapeVertex c)
        => PointInTriangle(point.X, point.Y, a, b, c);

    private static double Area(ShapeVertex a, ShapeVertex b, ShapeVertex c)
        => Cross(a, b, c) / 2;

    private static double UnwrapNear(double reference, double longitude)
    {
        while (longitude - reference > 180) longitude -= 360;
        while (longitude - reference < -180) longitude += 360;
        return longitude;
    }

    private static double ToRadians(double value) => value * (Math.PI / 180d);

    private readonly record struct ParameterInterval(double Minimum, double Maximum);
}

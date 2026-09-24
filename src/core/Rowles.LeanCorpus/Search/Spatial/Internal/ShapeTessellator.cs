using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;

/// <summary>Prepares built-in Geo and XY geometries as deterministic Packed BKD values.</summary>
internal static class ShapeTessellator
{
    private const int MaximumInputVertices = 100_000;
    private const int MaximumGeometryComponents = 100_000;
    private const int MaximumOutputPrimitives = 1_000_000;

    internal static void ValidateGeoFieldGeometry(IGeoGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        CheckGeometryComplexity(geometry);
        switch (geometry)
        {
            case GeoPoint or GeoRectangle or GeoLineString or GeoPolygon:
                return;
            case GeoGeometryCollection collection:
                foreach (IGeoGeometry component in collection.Geometries)
                    if (component is GeoCircle || component is not (GeoPoint or GeoRectangle or GeoLineString or GeoPolygon))
                        throw new ArgumentException("Shape fields accept only built-in non-circle geographic geometries.", nameof(geometry));
                return;
            default:
                throw new ArgumentException("Shape fields accept only built-in non-circle geographic geometries.", nameof(geometry));
        }
    }

    internal static void ValidateXYFieldGeometry(IXYGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        CheckGeometryComplexity(geometry);
        switch (geometry)
        {
            case XYPoint or XYRectangle or XYLineString or XYPolygon:
                return;
            case XYGeometryCollection collection:
                foreach (IXYGeometry component in collection.Geometries)
                    if (component is XYCircle || component is not (XYPoint or XYRectangle or XYLineString or XYPolygon))
                        throw new ArgumentException("Shape fields accept only built-in non-circle Cartesian geometries.", nameof(geometry));
                return;
            default:
                throw new ArgumentException("Shape fields accept only built-in non-circle Cartesian geometries.", nameof(geometry));
        }
    }

    internal static void ValidateGeoQueryGeometry(IGeoGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        CheckGeometryComplexity(geometry);
        switch (geometry)
        {
            case GeoPoint or GeoRectangle or GeoCircle or GeoLineString or GeoPolygon:
                return;
            case GeoGeometryCollection collection:
                foreach (IGeoGeometry component in collection.Geometries)
                    if (component is not (GeoPoint or GeoRectangle or GeoCircle or GeoLineString or GeoPolygon))
                        throw new ArgumentException("Shape queries accept only built-in geographic geometries.", nameof(geometry));
                return;
            default:
                throw new ArgumentException("Shape queries accept only built-in geographic geometries.", nameof(geometry));
        }
    }

    internal static void ValidateXYQueryGeometry(IXYGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        CheckGeometryComplexity(geometry);
        switch (geometry)
        {
            case XYPoint or XYRectangle or XYCircle or XYLineString or XYPolygon:
                return;
            case XYGeometryCollection collection:
                foreach (IXYGeometry component in collection.Geometries)
                    if (component is not (XYPoint or XYRectangle or XYCircle or XYLineString or XYPolygon))
                        throw new ArgumentException("Shape queries accept only built-in Cartesian geometries.", nameof(geometry));
                return;
            default:
                throw new ArgumentException("Shape queries accept only built-in Cartesian geometries.", nameof(geometry));
        }
    }

    internal static List<ShapePrimitive> PrepareGeo(IGeoGeometry geometry, uint valueOrdinal)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        CheckGeometryComplexity(geometry);
        var output = new List<ShapePrimitive>();
        switch (geometry)
        {
            case GeoPoint point:
                AddPoint(output, SpatialFieldKind.GeoShape, point.Longitude, point.Latitude, valueOrdinal);
                break;
            case GeoRectangle rectangle:
                AppendGeoRectangle(output, rectangle, valueOrdinal);
                break;
            case GeoLineString line:
                AppendGeoLine(output, line.Points, valueOrdinal);
                break;
            case GeoPolygon polygon:
                AppendGeoPolygon(output, polygon.Shell, polygon.Holes, valueOrdinal);
                break;
            case GeoGeometryCollection collection:
                foreach (IGeoGeometry component in collection.Geometries)
                    AppendGeoComponent(output, component, valueOrdinal);
                break;
            case GeoCircle:
                throw new ArgumentException("Circles are query-only and cannot be indexed as shape fields.", nameof(geometry));
            default:
                throw new ArgumentException("Shape fields accept only built-in geographic geometries.", nameof(geometry));
        }

        CheckOutputCount(output.Count);
        return output;
    }

    internal static List<ShapePrimitive> PrepareXY(IXYGeometry geometry, uint valueOrdinal)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        CheckGeometryComplexity(geometry);
        var output = new List<ShapePrimitive>();
        switch (geometry)
        {
            case XYPoint point:
                AddPoint(output, SpatialFieldKind.XYShape, point.X, point.Y, valueOrdinal);
                break;
            case XYRectangle rectangle:
                AppendXYRectangle(output, rectangle, valueOrdinal);
                break;
            case XYLineString line:
                AppendXYLine(output, line.Points, valueOrdinal);
                break;
            case XYPolygon polygon:
                AppendXYPolygon(output, polygon.Shell, polygon.Holes, valueOrdinal);
                break;
            case XYGeometryCollection collection:
                foreach (IXYGeometry component in collection.Geometries)
                    AppendXYComponent(output, component, valueOrdinal);
                break;
            case XYCircle:
                throw new ArgumentException("Circles are query-only and cannot be indexed as shape fields.", nameof(geometry));
            default:
                throw new ArgumentException("Shape fields accept only built-in Cartesian geometries.", nameof(geometry));
        }

        CheckOutputCount(output.Count);
        return output;
    }

    private static void AppendGeoComponent(List<ShapePrimitive> output, IGeoGeometry geometry, uint ordinal)
    {
        switch (geometry)
        {
            case GeoPoint point:
                AddPoint(output, SpatialFieldKind.GeoShape, point.Longitude, point.Latitude, ordinal);
                break;
            case GeoRectangle rectangle:
                AppendGeoRectangle(output, rectangle, ordinal);
                break;
            case GeoLineString line:
                AppendGeoLine(output, line.Points, ordinal);
                break;
            case GeoPolygon polygon:
                AppendGeoPolygon(output, polygon.Shell, polygon.Holes, ordinal);
                break;
            case GeoCircle:
                throw new ArgumentException("Circles are query-only and cannot be indexed as shape fields.", nameof(geometry));
            default:
                throw new ArgumentException("Shape fields accept only built-in geographic geometries.", nameof(geometry));
        }
    }

    private static void AppendXYComponent(List<ShapePrimitive> output, IXYGeometry geometry, uint ordinal)
    {
        switch (geometry)
        {
            case XYPoint point:
                AddPoint(output, SpatialFieldKind.XYShape, point.X, point.Y, ordinal);
                break;
            case XYRectangle rectangle:
                AppendXYRectangle(output, rectangle, ordinal);
                break;
            case XYLineString line:
                AppendXYLine(output, line.Points, ordinal);
                break;
            case XYPolygon polygon:
                AppendXYPolygon(output, polygon.Shell, polygon.Holes, ordinal);
                break;
            case XYCircle:
                throw new ArgumentException("Circles are query-only and cannot be indexed as shape fields.", nameof(geometry));
            default:
                throw new ArgumentException("Shape fields accept only built-in Cartesian geometries.", nameof(geometry));
        }
    }

    private static void AppendGeoRectangle(List<ShapePrimitive> output, GeoRectangle rectangle, uint ordinal)
    {
        double west = QuantiseGeoLongitude(rectangle.West);
        double east = QuantiseGeoLongitude(rectangle.East) + (rectangle.CrossesDateline ? 360 : 0);
        double south = QuantiseGeoLatitude(rectangle.South);
        double north = QuantiseGeoLatitude(rectangle.North);
        if (south == north && west == east)
        {
            AddPoint(output, SpatialFieldKind.GeoShape, west, south, ordinal);
            return;
        }
        if (south == north)
        {
            AppendGeoLineCoordinates(output, [new WorkVertex(west, south, true), new WorkVertex(east, south, true)], ordinal);
            return;
        }
        if (west == east)
        {
            AppendGeoLineCoordinates(output, [new WorkVertex(west, south, true), new WorkVertex(west, north, true)], ordinal);
            return;
        }

        var shell = new List<WorkVertex>
        {
            new(west, south, true),
            new(east, south, true),
            new(east, north, true),
            new(west, north, true),
        };
        AppendGeoPolygonWork(output, shell, [], ordinal);
    }

    private static void AppendXYRectangle(List<ShapePrimitive> output, XYRectangle rectangle, uint ordinal)
    {
        double minX = CanonicalXY(rectangle.MinX);
        double minY = CanonicalXY(rectangle.MinY);
        double maxX = CanonicalXY(rectangle.MaxX);
        double maxY = CanonicalXY(rectangle.MaxY);
        if (minX == maxX && minY == maxY)
        {
            AddPoint(output, SpatialFieldKind.XYShape, minX, minY, ordinal);
            return;
        }
        if (minY == maxY)
        {
            AddLine(output, SpatialFieldKind.XYShape, new WorkVertex(minX, minY, true), new WorkVertex(maxX, maxY, true), ordinal);
            return;
        }
        if (minX == maxX)
        {
            AddLine(output, SpatialFieldKind.XYShape, new WorkVertex(minX, minY, true), new WorkVertex(maxX, maxY, true), ordinal);
            return;
        }

        var shell = new List<WorkVertex>
        {
            new(minX, minY, true),
            new(maxX, minY, true),
            new(maxX, maxY, true),
            new(minX, maxY, true),
        };
        AppendXYPolygonWork(output, shell, [], ordinal);
    }

    private static void AppendGeoLine(List<ShapePrimitive> output, IReadOnlyList<GeoPoint> points, uint ordinal)
    {
        var unwrapped = CreateGeoRing(points, referenceLongitude: null, requireClosed: false);
        AppendGeoLineCoordinates(output, unwrapped, ordinal);
    }

    private static void AppendGeoLineCoordinates(List<ShapePrimitive> output, IReadOnlyList<WorkVertex> points, uint ordinal)
    {
        for (int i = 0; i + 1 < points.Count; i++)
            SplitGeoLineSegment(output, points[i], points[i + 1], ordinal);
    }

    private static void SplitGeoLineSegment(List<ShapePrimitive> output, WorkVertex first, WorkVertex second, uint ordinal)
    {
        if (first.X == second.X && first.Y == second.Y)
        {
            AddPoint(output, SpatialFieldKind.GeoShape, first.X, first.Y, ordinal);
            return;
        }

        int firstStrip = StripIndex(first.X);
        int secondStrip = StripIndex(second.X);
        if (firstStrip == secondStrip)
        {
            AddLine(output, SpatialFieldKind.GeoShape,
                new WorkVertex(first.X - (360d * firstStrip), first.Y, true),
                new WorkVertex(second.X - (360d * firstStrip), second.Y, true), ordinal);
            return;
        }

        int stripStep = secondStrip > firstStrip ? 1 : -1;
        double seam = stripStep > 0 ? 180d + 360d * firstStrip : -180d + 360d * firstStrip;
        double fraction = (seam - first.X) / (second.X - first.X);
        double seamY = first.Y + (second.Y - first.Y) * fraction;
        if (first.X != seam || first.Y != seamY)
            AddLine(output, SpatialFieldKind.GeoShape,
                new WorkVertex(first.X - (360d * firstStrip), first.Y, true),
                new WorkVertex(seam - (360d * firstStrip), seamY, true), ordinal);
        if (second.X != seam || second.Y != seamY)
            AddLine(output, SpatialFieldKind.GeoShape,
                new WorkVertex(seam - (360d * secondStrip), seamY, true),
                new WorkVertex(second.X - (360d * secondStrip), second.Y, true), ordinal);
    }

    private static void AppendXYLine(List<ShapePrimitive> output, IReadOnlyList<XYPoint> points, uint ordinal)
    {
        CheckVertexCount(points.Count);
        for (int i = 0; i + 1 < points.Count; i++)
        {
            var first = new WorkVertex(CanonicalXY(points[i].X), CanonicalXY(points[i].Y), true);
            var second = new WorkVertex(CanonicalXY(points[i + 1].X), CanonicalXY(points[i + 1].Y), true);
            AddLine(output, SpatialFieldKind.XYShape, first, second, ordinal);
        }
    }

    private static void AppendGeoPolygon(
        List<ShapePrimitive> output,
        IReadOnlyList<GeoPoint> shellPoints,
        IReadOnlyList<IReadOnlyList<GeoPoint>> holes,
        uint ordinal)
    {
        List<WorkVertex> shell = CreateGeoRing(shellPoints, referenceLongitude: null, requireClosed: true);
        var workHoles = new List<List<WorkVertex>>(holes.Count);
        foreach (IReadOnlyList<GeoPoint> hole in holes)
            workHoles.Add(CreateGeoRing(hole, shell[0].X, requireClosed: true));
        AppendGeoPolygonWork(output, shell, workHoles, ordinal);
    }

    private static void AppendXYPolygon(
        List<ShapePrimitive> output,
        IReadOnlyList<XYPoint> shellPoints,
        IReadOnlyList<IReadOnlyList<XYPoint>> holes,
        uint ordinal)
    {
        List<WorkVertex> shell = CreateXYRing(shellPoints);
        var workHoles = new List<List<WorkVertex>>(holes.Count);
        foreach (IReadOnlyList<XYPoint> hole in holes)
            workHoles.Add(CreateXYRing(hole));
        AppendXYPolygonWork(output, shell, workHoles, ordinal);
    }

    private static void AppendGeoPolygonWork(
        List<ShapePrimitive> output,
        List<WorkVertex> shell,
        List<List<WorkVertex>> holes,
        uint ordinal)
    {
        List<WorkTriangle> triangles = Triangulate(shell, holes);
        foreach (WorkTriangle triangle in triangles)
            SplitGeoTriangle(output, triangle, ordinal);
    }

    private static void AppendXYPolygonWork(
        List<ShapePrimitive> output,
        List<WorkVertex> shell,
        List<List<WorkVertex>> holes,
        uint ordinal)
    {
        List<WorkTriangle> triangles = Triangulate(shell, holes);
        foreach (WorkTriangle triangle in triangles)
        {
            ShapeVertex a = ShapePrimitiveCodec.Quantise(new ShapeVertex(triangle.A.X, triangle.A.Y), SpatialFieldKind.XYShape);
            ShapeVertex b = ShapePrimitiveCodec.Quantise(new ShapeVertex(triangle.B.X, triangle.B.Y), SpatialFieldKind.XYShape);
            ShapeVertex c = ShapePrimitiveCodec.Quantise(new ShapeVertex(triangle.C.X, triangle.C.Y), SpatialFieldKind.XYShape);
            if (SignedArea(a.X, a.Y, b.X, b.Y, c.X, c.Y) <= 0)
                continue;
            AddTriangle(output, a, triangle.EdgeAB, b, triangle.EdgeBC, c, triangle.EdgeCA, ordinal);
        }
    }

    private static List<WorkTriangle> Triangulate(List<WorkVertex> shell, List<List<WorkVertex>> holes)
    {
        long inputVertexCount = shell.Count;
        foreach (List<WorkVertex> hole in holes)
        {
            inputVertexCount = checked(inputVertexCount + hole.Count);
            if (inputVertexCount > MaximumInputVertices)
                throw new ArgumentException($"A polygon cannot contain more than {MaximumInputVertices} shell and hole vertices.");
        }
        NormaliseRing(shell, counterClockwise: true, "shell");
        foreach (List<WorkVertex> hole in holes)
            NormaliseRing(hole, counterClockwise: false, "hole");
        ValidateHoles(shell, holes);

        List<WorkTriangle> triangles;
        if (holes.Count == 0 && TryTriangulateConvex(shell, out List<WorkTriangle> convexTriangles))
            triangles = convexTriangles;
        else
        {
            var merged = new List<WorkVertex>(shell);
            foreach (List<WorkVertex> hole in holes
                .OrderByDescending(static ring => ring[RightmostIndex(ring)].X)
                .ThenBy(static ring => ring[RightmostIndex(ring)].Y))
                merged = BridgeHole(merged, hole, shell, holes);
            triangles = EarClip(merged);
        }

        double expectedArea = Math.Abs(Area(shell));
        foreach (List<WorkVertex> hole in holes)
            expectedArea -= Math.Abs(Area(hole));
        double actualArea = 0;
        foreach (WorkTriangle triangle in triangles)
            actualArea += SignedArea(triangle.A.X, triangle.A.Y, triangle.B.X, triangle.B.Y, triangle.C.X, triangle.C.Y) / 2;
        double tolerance = Math.Max(1e-12, Math.Abs(expectedArea) * 1e-10);
        if (expectedArea <= 0 || Math.Abs(actualArea - expectedArea) > tolerance)
            throw new ArgumentException("Shape tessellation did not preserve the polygon area.");
        return triangles;
    }

    private static bool TryTriangulateConvex(List<WorkVertex> polygon, out List<WorkTriangle> triangles)
    {
        triangles = [];
        if (polygon.Count < 3)
            return false;
        for (int i = 0; i < polygon.Count; i++)
        {
            WorkVertex a = polygon[i];
            WorkVertex b = polygon[(i + 1) % polygon.Count];
            WorkVertex c = polygon[(i + 2) % polygon.Count];
            if (SignedArea(a.X, a.Y, b.X, b.Y, c.X, c.Y) <= 0)
                return false;
        }

        triangles = new List<WorkTriangle>(polygon.Count - 2);
        for (int i = 1; i + 1 < polygon.Count; i++)
        {
            WorkVertex a = polygon[0];
            WorkVertex b = polygon[i];
            WorkVertex c = polygon[i + 1];
            triangles.Add(new WorkTriangle(
                a,
                b,
                c,
                i == 1 && a.SourceToNext,
                b.SourceToNext,
                i + 1 == polygon.Count - 1 && c.SourceToNext));
        }
        return true;
    }

    private static void NormaliseRing(List<WorkVertex> ring, bool counterClockwise, string parameterName)
    {
        if (ring.Count > 1 && SamePosition(ring[0], ring[^1].X, ring[^1].Y))
            ring.RemoveAt(ring.Count - 1);

        for (int i = 0; i < ring.Count && ring.Count > 1;)
        {
            int next = (i + 1) % ring.Count;
            if (SamePosition(ring[i], ring[next].X, ring[next].Y))
            {
                if (next == 0)
                {
                    ring[0] = ring[0] with { SourceToNext = ring[^1].SourceToNext };
                    ring.RemoveAt(ring.Count - 1);
                }
                else
                {
                    ring[i] = ring[i] with { SourceToNext = ring[i].SourceToNext && ring[next].SourceToNext };
                    ring.RemoveAt(next);
                }
            }
            else
                i++;
        }

        bool changed;
        do
        {
            changed = false;
            if (ring.Count < 3)
                break;
            for (int i = 0; i < ring.Count; i++)
            {
                int previous = (i + ring.Count - 1) % ring.Count;
                int next = (i + 1) % ring.Count;
                WorkVertex a = ring[previous];
                WorkVertex b = ring[i];
                WorkVertex c = ring[next];
                if (SignedArea(a.X, a.Y, b.X, b.Y, c.X, c.Y) != 0 || !OnSegment(a, c, b))
                    continue;
                ring[previous] = a with { SourceToNext = a.SourceToNext && b.SourceToNext };
                ring.RemoveAt(i);
                changed = true;
                break;
            }
        } while (changed);

        if (ring.Count < 3 || Math.Abs(Area(ring)) <= 1e-12)
            throw new ArgumentException("A polygon ring must retain at least three non-collinear quantised vertices.", parameterName);

        bool isCounterClockwise = Area(ring) > 0;
        if (isCounterClockwise != counterClockwise)
            ReverseRing(ring);
    }

    private static void ReverseRing(List<WorkVertex> ring)
    {
        var reversed = new WorkVertex[ring.Count];
        for (int i = 0; i < ring.Count; i++)
        {
            int oldIndex = ring.Count - 1 - i;
            int oldPrevious = (oldIndex + ring.Count - 1) % ring.Count;
            reversed[i] = ring[oldIndex] with { SourceToNext = ring[oldPrevious].SourceToNext };
        }
        ring.Clear();
        ring.AddRange(reversed);
    }

    private static void ValidateHoles(IReadOnlyList<WorkVertex> shell, IReadOnlyList<List<WorkVertex>> holes)
    {
        for (int i = 0; i < holes.Count; i++)
        {
            List<WorkVertex> hole = holes[i];
            if (!PointInRing(hole[0].X, hole[0].Y, shell) || RingsIntersect(shell, hole))
                throw new ArgumentException("A quantised polygon hole must lie inside the shell without touching its boundary.");
            for (int j = 0; j < i; j++)
            {
                List<WorkVertex> previous = holes[j];
                if (RingsIntersect(hole, previous)
                    || PointInRing(hole[0].X, hole[0].Y, previous)
                    || PointInRing(previous[0].X, previous[0].Y, hole))
                    throw new ArgumentException("Quantised polygon holes must not touch or overlap.");
            }
        }
    }

    private static List<WorkVertex> BridgeHole(
        List<WorkVertex> outer,
        List<WorkVertex> hole,
        IReadOnlyList<WorkVertex> originalShell,
        IReadOnlyList<List<WorkVertex>> allHoles)
    {
        int holeIndex = RightmostIndex(hole);
        WorkVertex holeVertex = hole[holeIndex];
        int outerIndex = -1;
        var candidates = new List<BridgeCandidate>(outer.Count);
        for (int candidateIndex = 0; candidateIndex < outer.Count; candidateIndex++)
        {
            WorkVertex candidate = outer[candidateIndex];
            double dx = candidate.X - holeVertex.X;
            double dy = candidate.Y - holeVertex.Y;
            double distance = dx * dx + dy * dy;
            candidates.Add(new BridgeCandidate(distance, candidate.X, candidate.Y, candidateIndex));
        }
        candidates.Sort(static (first, second) =>
        {
            int comparison = first.DistanceSquared.CompareTo(second.DistanceSquared);
            if (comparison != 0)
                return comparison;
            comparison = first.X.CompareTo(second.X);
            if (comparison != 0)
                return comparison;
            comparison = first.Y.CompareTo(second.Y);
            return comparison != 0 ? comparison : first.Index.CompareTo(second.Index);
        });
        foreach (BridgeCandidate candidate in candidates)
        {
            if (BridgeIsVisible(holeVertex, outer[candidate.Index], outer, hole, originalShell, allHoles))
            {
                outerIndex = candidate.Index;
                break;
            }
        }

        if (outerIndex < 0)
            throw new ArgumentException("A polygon hole has no visible bridge to the shell.");

        var merged = new List<WorkVertex>(checked(outer.Count + hole.Count + 2));
        for (int i = 0; i <= outerIndex; i++)
        {
            WorkVertex vertex = outer[i];
            if (i == outerIndex)
                vertex = vertex with { SourceToNext = false };
            merged.Add(vertex);
        }

        for (int i = 0; i < hole.Count; i++)
            merged.Add(hole[(holeIndex + i) % hole.Count]);
        WorkVertex duplicateHole = hole[holeIndex] with { SourceToNext = false };
        merged.Add(duplicateHole);
        WorkVertex duplicateOuter = outer[outerIndex] with { SourceToNext = outer[outerIndex].SourceToNext };
        merged.Add(duplicateOuter);

        for (int i = outerIndex + 1; i < outer.Count; i++)
            merged.Add(outer[i]);
        return merged;
    }

    private static bool BridgeIsVisible(
        WorkVertex holeVertex,
        WorkVertex outerVertex,
        IReadOnlyList<WorkVertex> currentOuter,
        IReadOnlyList<WorkVertex> currentHole,
        IReadOnlyList<WorkVertex> originalShell,
        IReadOnlyList<List<WorkVertex>> allHoles)
    {
        if (SamePosition(holeVertex, outerVertex.X, outerVertex.Y))
            return false;
        if (RingHasIntersection(holeVertex, outerVertex, currentOuter)
            || RingHasIntersection(holeVertex, outerVertex, originalShell))
            return false;
        for (int i = 0; i < allHoles.Count; i++)
            if (RingHasIntersection(holeVertex, outerVertex, allHoles[i]))
                return false;

        double midpointX = (holeVertex.X + outerVertex.X) / 2;
        double midpointY = (holeVertex.Y + outerVertex.Y) / 2;
        if (!PointInRing(midpointX, midpointY, originalShell))
            return false;
        foreach (List<WorkVertex> hole in allHoles)
            if (PointInRing(midpointX, midpointY, hole))
                return false;
        return !PointInRing(midpointX, midpointY, currentHole);
    }

    private static bool RingHasIntersection(double ax, double ay, double bx, double by, IReadOnlyList<WorkVertex> ring)
    {
        for (int i = 0; i < ring.Count; i++)
        {
            WorkVertex first = ring[i];
            WorkVertex second = ring[(i + 1) % ring.Count];
            if ((SamePosition(first, ax, ay) || SamePosition(first, bx, by)
                || SamePosition(second, ax, ay) || SamePosition(second, bx, by)))
                continue;
            if (SegmentsIntersect(ax, ay, bx, by, first.X, first.Y, second.X, second.Y))
                return true;
        }
        return false;
    }

    private static bool RingHasIntersection(WorkVertex a, WorkVertex b, IReadOnlyList<WorkVertex> ring)
        => RingHasIntersection(a.X, a.Y, b.X, b.Y, ring);

    private static int RightmostIndex(IReadOnlyList<WorkVertex> ring)
    {
        int best = 0;
        for (int i = 1; i < ring.Count; i++)
            if (ring[i].X > ring[best].X || (ring[i].X == ring[best].X && ring[i].Y < ring[best].Y))
                best = i;
        return best;
    }

    private static List<WorkTriangle> EarClip(List<WorkVertex> polygon)
    {
        if (polygon.Count > MaximumInputVertices * 2)
            throw new ArgumentException("A polygon with holes exceeds the bounded tessellation vertex count.");
        int[] previous = new int[polygon.Count];
        int[] next = new int[polygon.Count];
        var active = new bool[polygon.Count];
        for (int i = 0; i < polygon.Count; i++)
        {
            previous[i] = (i + polygon.Count - 1) % polygon.Count;
            next[i] = (i + 1) % polygon.Count;
            active[i] = true;
        }
        var triangles = new List<WorkTriangle>(Math.Max(1, polygon.Count - 2));
        EarZOrderIndex? zOrder = polygon.Count >= 64 ? new EarZOrderIndex(polygon) : null;
        long maximumIterations = (long)polygon.Count * polygon.Count + polygon.Count;
        long iterations = 0;
        int activeCount = polygon.Count;
        int head = 0;
        while (activeCount > 3)
        {
            bool removed = false;
            int currentIndex = head;
            for (int position = 0; position < activeCount; position++)
            {
                if (++iterations > maximumIterations)
                    throw new ArgumentException("Shape tessellation exceeded its bounded ear-clipping work limit.");
                int previousIndex = previous[currentIndex];
                int nextIndex = next[currentIndex];
                WorkVertex a = polygon[previousIndex];
                WorkVertex b = polygon[currentIndex];
                WorkVertex c = polygon[nextIndex];
                double area = SignedArea(a.X, a.Y, b.X, b.Y, c.X, c.Y);
                if (area == 0 && OnSegment(a, c, b))
                {
                    polygon[previousIndex] = a with { SourceToNext = a.SourceToNext && b.SourceToNext };
                    RemoveEarVertex(currentIndex, previousIndex, nextIndex, previous, next, active);
                    activeCount--;
                    if (currentIndex == head)
                        head = nextIndex;
                    removed = true;
                    break;
                }
                if (area <= 0 || !IsEar(
                    previousIndex, currentIndex, nextIndex, head, activeCount,
                    next, active, polygon, zOrder))
                {
                    currentIndex = next[currentIndex];
                    continue;
                }

                triangles.Add(new WorkTriangle(a, b, c, a.SourceToNext, b.SourceToNext, false));
                polygon[previousIndex] = a with { SourceToNext = false };
                RemoveEarVertex(currentIndex, previousIndex, nextIndex, previous, next, active);
                activeCount--;
                if (currentIndex == head)
                    head = nextIndex;
                removed = true;
                break;
            }

            if (!removed)
                throw new ArgumentException("Shape tessellation could not find a valid ear in the polygon.");
        }

        int finalIndexA = head;
        int finalIndexB = next[finalIndexA];
        int finalIndexC = next[finalIndexB];
        WorkVertex finalA = polygon[finalIndexA];
        WorkVertex finalB = polygon[finalIndexB];
        WorkVertex finalC = polygon[finalIndexC];
        if (SignedArea(finalA.X, finalA.Y, finalB.X, finalB.Y, finalC.X, finalC.Y) <= 0)
            throw new ArgumentException("Shape tessellation ended with a degenerate polygon remainder.");
        triangles.Add(new WorkTriangle(
            finalA, finalB, finalC,
            finalA.SourceToNext, finalB.SourceToNext, finalC.SourceToNext));
        return triangles;
    }

    private static void RemoveEarVertex(
        int current,
        int previousIndex,
        int nextIndex,
        int[] previous,
        int[] next,
        bool[] active)
    {
        next[previousIndex] = nextIndex;
        previous[nextIndex] = previousIndex;
        active[current] = false;
    }

    private static bool IsEar(
        int previousIndex,
        int currentIndex,
        int nextIndex,
        int head,
        int activeCount,
        int[] next,
        bool[] active,
        IReadOnlyList<WorkVertex> polygon,
        EarZOrderIndex? zOrder)
    {
        WorkVertex a = polygon[previousIndex];
        WorkVertex b = polygon[currentIndex];
        WorkVertex c = polygon[nextIndex];
        double midpointX = (a.X + c.X) / 2;
        double midpointY = (a.Y + c.Y) / 2;
        if (DiagonalIntersects(a, c, previousIndex, nextIndex, head, activeCount, next, polygon)
            || !PointInRing(midpointX, midpointY, head, activeCount, next, polygon))
            return false;

        if (zOrder is not null)
        {
            return !zOrder.ContainsInteriorVertex(a, b, c, previousIndex, currentIndex, nextIndex, active, polygon);
        }

        int vertexIndex = head;
        for (int i = 0; i < activeCount; i++)
        {
            if (vertexIndex != previousIndex && vertexIndex != currentIndex && vertexIndex != nextIndex)
            {
                WorkVertex point = polygon[vertexIndex];
                if (!SamePosition(point, a.X, a.Y) && !SamePosition(point, b.X, b.Y) && !SamePosition(point, c.X, c.Y)
                    && PointInTriangle(point, a, b, c))
                    return false;
            }
            vertexIndex = next[vertexIndex];
        }
        return true;
    }

    private static bool PointInTriangle(WorkVertex point, WorkVertex a, WorkVertex b, WorkVertex c)
    {
        double ab = SignedArea(a.X, a.Y, b.X, b.Y, point.X, point.Y);
        double bc = SignedArea(b.X, b.Y, c.X, c.Y, point.X, point.Y);
        double ca = SignedArea(c.X, c.Y, a.X, a.Y, point.X, point.Y);
        return ab >= 0 && bc >= 0 && ca >= 0;
    }

    private static bool PointInRing(double x, double y, IReadOnlyList<WorkVertex> ring)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            WorkVertex a = ring[i];
            WorkVertex b = ring[j];
            if (OnSegment(a, b, x, y))
                return true;
            if (((a.Y > y) != (b.Y > y))
                && x < ((b.X - a.X) * (y - a.Y) / (b.Y - a.Y)) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static bool PointInRing(
        double x,
        double y,
        int head,
        int activeCount,
        int[] next,
        IReadOnlyList<WorkVertex> polygon)
    {
        bool inside = false;
        int first = head;
        for (int i = 0; i < activeCount; i++)
        {
            int second = next[first];
            WorkVertex a = polygon[first];
            WorkVertex b = polygon[second];
            if (OnSegment(a, b, x, y))
                return true;
            if (((a.Y > y) != (b.Y > y))
                && x < ((b.X - a.X) * (y - a.Y) / (b.Y - a.Y)) + a.X)
                inside = !inside;
            first = second;
        }
        return inside;
    }

    private static bool DiagonalIntersects(
        WorkVertex a,
        WorkVertex b,
        int previousIndex,
        int nextIndex,
        int head,
        int activeCount,
        int[] next,
        IReadOnlyList<WorkVertex> polygon)
    {
        int edgeStart = head;
        for (int i = 0; i < activeCount; i++)
        {
            int edgeEnd = next[edgeStart];
            if (edgeStart == previousIndex || edgeStart == nextIndex
                || edgeEnd == previousIndex || edgeEnd == nextIndex)
            {
                edgeStart = edgeEnd;
                continue;
            }
            WorkVertex first = polygon[edgeStart];
            WorkVertex second = polygon[edgeEnd];
            if (SamePosition(first, a.X, a.Y) || SamePosition(first, b.X, b.Y)
                || SamePosition(second, a.X, a.Y) || SamePosition(second, b.X, b.Y))
            {
                edgeStart = edgeEnd;
                continue;
            }
            if (Math.Max(a.X, b.X) < Math.Min(first.X, second.X)
                || Math.Min(a.X, b.X) > Math.Max(first.X, second.X)
                || Math.Max(a.Y, b.Y) < Math.Min(first.Y, second.Y)
                || Math.Min(a.Y, b.Y) > Math.Max(first.Y, second.Y))
            {
                edgeStart = edgeEnd;
                continue;
            }
            if (SegmentsIntersect(a.X, a.Y, b.X, b.Y, first.X, first.Y, second.X, second.Y))
                return true;
            edgeStart = edgeEnd;
        }
        return false;
    }

    private static bool RingsIntersect(IReadOnlyList<WorkVertex> first, IReadOnlyList<WorkVertex> second)
    {
        for (int i = 0; i < first.Count; i++)
        {
            WorkVertex a = first[i];
            WorkVertex b = first[(i + 1) % first.Count];
            for (int j = 0; j < second.Count; j++)
            {
                WorkVertex c = second[j];
                WorkVertex d = second[(j + 1) % second.Count];
                if (SegmentsIntersect(a.X, a.Y, b.X, b.Y, c.X, c.Y, d.X, d.Y))
                    return true;
            }
        }
        return false;
    }

    private static bool SegmentsIntersect(
        double ax,
        double ay,
        double bx,
        double by,
        double cx,
        double cy,
        double dx,
        double dy)
    {
        double abc = SignedArea(ax, ay, bx, by, cx, cy);
        double abd = SignedArea(ax, ay, bx, by, dx, dy);
        double cda = SignedArea(cx, cy, dx, dy, ax, ay);
        double cdb = SignedArea(cx, cy, dx, dy, bx, by);
        if (abc == 0 && OnSegment(ax, ay, bx, by, cx, cy)) return true;
        if (abd == 0 && OnSegment(ax, ay, bx, by, dx, dy)) return true;
        if (cda == 0 && OnSegment(cx, cy, dx, dy, ax, ay)) return true;
        if (cdb == 0 && OnSegment(cx, cy, dx, dy, bx, by)) return true;
        return (abc > 0) != (abd > 0) && (cda > 0) != (cdb > 0);
    }

    private static void SplitGeoTriangle(List<ShapePrimitive> output, WorkTriangle triangle, uint ordinal)
    {
        double minimumX = Math.Min(triangle.A.X, Math.Min(triangle.B.X, triangle.C.X));
        double maximumX = Math.Max(triangle.A.X, Math.Max(triangle.B.X, triangle.C.X));
        int firstStrip = StripIndex(minimumX);
        int lastStrip = StripIndex(maximumX);
        if (lastStrip - firstStrip > 1)
            throw new ArgumentException("A geographic shape primitive cannot span more than one Date Line seam.");

        for (int strip = firstStrip; strip <= lastStrip; strip++)
        {
            var clipped = new List<ClipVertex>(5)
            {
                new(triangle.A with { SourceToNext = triangle.EdgeAB }, triangle.EdgeCA),
                new(triangle.B with { SourceToNext = triangle.EdgeBC }, triangle.EdgeAB),
                new(triangle.C with { SourceToNext = triangle.EdgeCA }, triangle.EdgeBC),
            };
            clipped = ClipToLongitude(clipped, -180d + 360d * strip, keepGreater: true);
            if (clipped.Count < 3)
                continue;
            clipped = ClipToLongitude(clipped, 180d + 360d * strip, keepGreater: false);
            if (clipped.Count < 3)
                continue;

            var vertices = new List<WorkVertex>(clipped.Count);
            for (int i = 0; i < clipped.Count; i++)
            {
                ClipVertex current = clipped[i];
                ClipVertex next = clipped[(i + 1) % clipped.Count];
                double shiftedLongitude = current.Vertex.X - (360d * strip);
                if (vertices.Count == 0 || !SamePosition(vertices[^1], shiftedLongitude, current.Vertex.Y))
                    vertices.Add(new WorkVertex(shiftedLongitude, current.Vertex.Y, next.IncomingSource));
                else
                    vertices[^1] = vertices[^1] with { SourceToNext = next.IncomingSource };
            }
            if (vertices.Count > 1 && SamePosition(vertices[0], vertices[^1].X, vertices[^1].Y))
                vertices.RemoveAt(vertices.Count - 1);
            if (vertices.Count < 3)
                continue;

            for (int i = 1; i + 1 < vertices.Count; i++)
            {
                WorkVertex a = vertices[0];
                WorkVertex b = vertices[i];
                WorkVertex c = vertices[i + 1];
                ShapeVertex encodedA = ShapePrimitiveCodec.Quantise(new ShapeVertex(a.X, a.Y), SpatialFieldKind.GeoShape);
                ShapeVertex encodedB = ShapePrimitiveCodec.Quantise(new ShapeVertex(b.X, b.Y), SpatialFieldKind.GeoShape);
                ShapeVertex encodedC = ShapePrimitiveCodec.Quantise(new ShapeVertex(c.X, c.Y), SpatialFieldKind.GeoShape);
                if (SignedArea(encodedA.X, encodedA.Y, encodedB.X, encodedB.Y, encodedC.X, encodedC.Y) <= 0)
                    continue;

                bool edgeAB = i == 1 && a.SourceToNext;
                bool edgeBC = b.SourceToNext;
                bool edgeCA = i + 1 == vertices.Count - 1 && c.SourceToNext;
                AddTriangle(output, encodedA, edgeAB, encodedB, edgeBC, encodedC, edgeCA, ordinal);
            }
        }
    }

    private static List<ClipVertex> ClipToLongitude(List<ClipVertex> input, double boundary, bool keepGreater)
    {
        if (input.Count == 0)
            return input;
        var output = new List<ClipVertex>(input.Count + 2);
        for (int i = 0; i < input.Count; i++)
        {
            ClipVertex start = input[i];
            ClipVertex end = input[(i + 1) % input.Count];
            bool startInside = keepGreater ? start.Vertex.X >= boundary : start.Vertex.X <= boundary;
            bool endInside = keepGreater ? end.Vertex.X >= boundary : end.Vertex.X <= boundary;
            if (startInside && endInside)
            {
                AppendClipVertex(output, end with { IncomingSource = start.Vertex.SourceToNext });
            }
            else if (startInside)
            {
                AppendClipVertex(output, IntersectLongitude(start.Vertex, end.Vertex, boundary, start.Vertex.SourceToNext));
            }
            else if (endInside)
            {
                AppendClipVertex(output, IntersectLongitude(start.Vertex, end.Vertex, boundary, incomingSource: false));
                AppendClipVertex(output, end with { IncomingSource = start.Vertex.SourceToNext });
            }
        }

        if (output.Count > 1 && SamePosition(output[0].Vertex, output[^1].Vertex.X, output[^1].Vertex.Y))
        {
            output[0] = output[0] with { IncomingSource = output[^1].IncomingSource };
            output.RemoveAt(output.Count - 1);
        }
        return output;
    }

    private static ClipVertex IntersectLongitude(WorkVertex start, WorkVertex end, double longitude, bool incomingSource)
    {
        double fraction = (longitude - start.X) / (end.X - start.X);
        double latitude = start.Y + (end.Y - start.Y) * fraction;
        return new ClipVertex(new WorkVertex(longitude, latitude, false), incomingSource);
    }

    private static void AppendClipVertex(List<ClipVertex> output, ClipVertex vertex)
    {
        if (output.Count > 0 && SamePosition(output[^1].Vertex, vertex.Vertex.X, vertex.Vertex.Y))
        {
            output[^1] = output[^1] with { IncomingSource = output[^1].IncomingSource || vertex.IncomingSource };
            return;
        }
        output.Add(vertex);
    }

    private static bool OnSegment(WorkVertex a, WorkVertex b, WorkVertex point)
        => OnSegment(a.X, a.Y, b.X, b.Y, point.X, point.Y);

    private static bool OnSegment(WorkVertex a, WorkVertex b, double x, double y)
        => OnSegment(a.X, a.Y, b.X, b.Y, x, y);

    private static bool OnSegment(double ax, double ay, double bx, double by, double x, double y)
        => x >= Math.Min(ax, bx) && x <= Math.Max(ax, bx)
            && y >= Math.Min(ay, by) && y <= Math.Max(ay, by);

    private static double Area(IReadOnlyList<WorkVertex> ring)
    {
        double area = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            WorkVertex a = ring[i];
            WorkVertex b = ring[(i + 1) % ring.Count];
            area += a.X * b.Y - b.X * a.Y;
        }
        return area / 2;
    }

    private static List<WorkVertex> CreateGeoRing(
        IReadOnlyList<GeoPoint> points,
        double? referenceLongitude,
        bool requireClosed)
    {
        CheckVertexCount(points.Count);
        int count = points.Count;
        if (requireClosed && count > 1 && points[0].Equals(points[^1]))
            count--;
        var result = new List<WorkVertex>(count);
        double previousLongitude = referenceLongitude ?? 0;
        for (int i = 0; i < count; i++)
        {
            double latitude = QuantiseGeoLatitude(points[i].Latitude);
            double longitude = QuantiseGeoLongitude(points[i].Longitude);
            longitude = i == 0 && referenceLongitude.HasValue
                ? UnwrapNear(referenceLongitude.Value, longitude)
                : result.Count == 0 ? longitude : UnwrapNear(previousLongitude, longitude);
            previousLongitude = longitude;
            if (result.Count == 0 || !SamePosition(result[^1], longitude, latitude))
                result.Add(new WorkVertex(longitude, latitude, true));
        }

        if (requireClosed && result.Count > 1 && SamePosition(result[0], result[^1].X, result[^1].Y))
            result.RemoveAt(result.Count - 1);
        return result;
    }

    private static List<WorkVertex> CreateXYRing(IReadOnlyList<XYPoint> points)
    {
        CheckVertexCount(points.Count);
        int count = points.Count;
        if (count > 1 && points[0].Equals(points[^1]))
            count--;
        var result = new List<WorkVertex>(count);
        for (int i = 0; i < count; i++)
        {
            double x = CanonicalXY(points[i].X);
            double y = CanonicalXY(points[i].Y);
            if (result.Count == 0 || !SamePosition(result[^1], x, y))
                result.Add(new WorkVertex(x, y, true));
        }
        if (result.Count > 1 && SamePosition(result[0], result[^1].X, result[^1].Y))
            result.RemoveAt(result.Count - 1);
        return result;
    }

    private static void AddPoint(List<ShapePrimitive> output, SpatialFieldKind kind, double x, double y, uint ordinal)
    {
        ShapeVertex point = ShapePrimitiveCodec.Quantise(new ShapeVertex(x, y), kind);
        output.Add(new ShapePrimitive(point, point, point, true, true, true, ordinal, ShapePrimitiveKind.Point));
        CheckOutputCount(output.Count);
    }

    private static void AddLine(List<ShapePrimitive> output, SpatialFieldKind kind, WorkVertex first, WorkVertex second, uint ordinal)
    {
        ShapeVertex a = ShapePrimitiveCodec.Quantise(new ShapeVertex(first.X, first.Y), kind);
        ShapeVertex b = ShapePrimitiveCodec.Quantise(new ShapeVertex(second.X, second.Y), kind);
        if (a.XKey == b.XKey && a.YKey == b.YKey)
        {
            output.Add(new ShapePrimitive(a, a, a, true, true, true, ordinal, ShapePrimitiveKind.Point));
        }
        else
        {
            output.Add(new ShapePrimitive(a, b, a, true, true, true, ordinal, ShapePrimitiveKind.Line));
        }
        CheckOutputCount(output.Count);
    }

    private static void AddTriangle(
        List<ShapePrimitive> output,
        ShapeVertex a,
        bool edgeAB,
        ShapeVertex b,
        bool edgeBC,
        ShapeVertex c,
        bool edgeCA,
        uint ordinal)
    {
        output.Add(new ShapePrimitive(a, b, c, edgeAB, edgeBC, edgeCA, ordinal, ShapePrimitiveKind.Triangle));
        CheckOutputCount(output.Count);
    }

    private static void CheckVertexCount(int count)
    {
        if (count > MaximumInputVertices)
            throw new ArgumentException($"A shape component cannot contain more than {MaximumInputVertices} vertices.");
    }

    private static void CheckGeometryComplexity(IGeoGeometry geometry)
    {
        if (geometry is not GeoGeometryCollection collection)
        {
            CheckVertexCount(GeoVertexCount(geometry));
            return;
        }

        if (collection.Geometries.Count > MaximumGeometryComponents)
            throw new ArgumentException($"A shape geometry collection cannot contain more than {MaximumGeometryComponents} components.", nameof(geometry));
        long totalVertices = collection.Geometries.Count;
        foreach (IGeoGeometry component in collection.Geometries)
        {
            if (component is GeoGeometryCollection)
                throw new ArgumentException("Nested shape geometry collections are not supported.", nameof(geometry));
            totalVertices = checked(totalVertices + GeoVertexCount(component));
            if (totalVertices > MaximumInputVertices)
                throw new ArgumentException($"A shape value cannot contain more than {MaximumInputVertices} input vertices and components.", nameof(geometry));
        }
    }

    private static void CheckGeometryComplexity(IXYGeometry geometry)
    {
        if (geometry is not XYGeometryCollection collection)
        {
            CheckVertexCount(XYVertexCount(geometry));
            return;
        }

        if (collection.Geometries.Count > MaximumGeometryComponents)
            throw new ArgumentException($"A shape geometry collection cannot contain more than {MaximumGeometryComponents} components.", nameof(geometry));
        long totalVertices = collection.Geometries.Count;
        foreach (IXYGeometry component in collection.Geometries)
        {
            if (component is XYGeometryCollection)
                throw new ArgumentException("Nested shape geometry collections are not supported.", nameof(geometry));
            totalVertices = checked(totalVertices + XYVertexCount(component));
            if (totalVertices > MaximumInputVertices)
                throw new ArgumentException($"A shape value cannot contain more than {MaximumInputVertices} input vertices and components.", nameof(geometry));
        }
    }

    private static int GeoVertexCount(IGeoGeometry geometry)
        => geometry switch
        {
            GeoPoint => 1,
            GeoRectangle => 4,
            GeoLineString line => line.Points.Count,
            GeoPolygon polygon => checked(polygon.Shell.Count + polygon.Holes.Sum(static hole => hole.Count)),
            GeoCircle => 1,
            _ => throw new ArgumentException("Only built-in Geo geometries are supported.", nameof(geometry)),
        };

    private static int XYVertexCount(IXYGeometry geometry)
        => geometry switch
        {
            XYPoint => 1,
            XYRectangle => 4,
            XYLineString line => line.Points.Count,
            XYPolygon polygon => checked(polygon.Shell.Count + polygon.Holes.Sum(static hole => hole.Count)),
            XYCircle => 1,
            _ => throw new ArgumentException("Only built-in XY geometries are supported.", nameof(geometry)),
        };

    private static void CheckOutputCount(int count)
    {
        if (count > MaximumOutputPrimitives)
            throw new ArgumentException($"A shape value cannot emit more than {MaximumOutputPrimitives} primitives.");
    }

    private static double QuantiseGeoLongitude(double longitude)
        => GeoEncodingUtils.DecodeLon(GeoEncodingUtils.EncodeLon(longitude));

    private static double QuantiseGeoLatitude(double latitude)
        => GeoEncodingUtils.DecodeLat(GeoEncodingUtils.EncodeLat(latitude));

    private static double CanonicalXY(float value) => value == 0f ? 0d : value;

    private static bool SamePosition(WorkVertex vertex, double x, double y)
        => vertex.X == x && vertex.Y == y;

    private static double UnwrapNear(double reference, double longitude)
    {
        while (longitude - reference > 180)
            longitude -= 360;
        while (longitude - reference < -180)
            longitude += 360;
        return longitude;
    }

    private static int StripIndex(double longitude)
        => checked((int)Math.Floor((longitude + 180d) / 360d));

    private static double SignedArea(double ax, double ay, double bx, double by, double cx, double cy)
        => (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);

    private sealed class EarZOrderIndex
    {
        private readonly MortonVertex[] _vertices;
        private readonly double _minimumX;
        private readonly double _minimumY;
        private readonly double _maximumX;
        private readonly double _maximumY;

        internal EarZOrderIndex(IReadOnlyList<WorkVertex> polygon)
        {
            _minimumX = polygon.Min(static vertex => vertex.X);
            _minimumY = polygon.Min(static vertex => vertex.Y);
            _maximumX = polygon.Max(static vertex => vertex.X);
            _maximumY = polygon.Max(static vertex => vertex.Y);
            _vertices = new MortonVertex[polygon.Count];
            for (int i = 0; i < polygon.Count; i++)
                _vertices[i] = new MortonVertex(GetCode(polygon[i].X, polygon[i].Y), i);
            Array.Sort(_vertices, static (first, second) =>
            {
                int comparison = first.Code.CompareTo(second.Code);
                return comparison != 0 ? comparison : first.Index.CompareTo(second.Index);
            });
        }

        internal bool ContainsInteriorVertex(
            WorkVertex a,
            WorkVertex b,
            WorkVertex c,
            int previousIndex,
            int currentIndex,
            int nextIndex,
            bool[] active,
            IReadOnlyList<WorkVertex> polygon)
        {
            double minimumX = Math.Min(a.X, Math.Min(b.X, c.X));
            double minimumY = Math.Min(a.Y, Math.Min(b.Y, c.Y));
            double maximumX = Math.Max(a.X, Math.Max(b.X, c.X));
            double maximumY = Math.Max(a.Y, Math.Max(b.Y, c.Y));
            int start = LowerBound(GetCode(minimumX, minimumY));
            int end = UpperBound(GetCode(maximumX, maximumY));
            for (int position = start; position < end; position++)
            {
                int index = _vertices[position].Index;
                if (!active[index] || index == previousIndex || index == currentIndex || index == nextIndex)
                    continue;
                WorkVertex point = polygon[index];
                if (SamePosition(point, a.X, a.Y) || SamePosition(point, b.X, b.Y) || SamePosition(point, c.X, c.Y))
                    continue;
                if (PointInTriangle(point, a, b, c))
                    return true;
            }
            return false;
        }

        private ulong GetCode(double x, double y)
            => Interleave(Normalise(x, _minimumX, _maximumX), Normalise(y, _minimumY, _maximumY));

        private static uint Normalise(double value, double minimum, double maximum)
        {
            if (maximum <= minimum)
                return 0;
            double unit = Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);
            return (uint)Math.Floor(unit * uint.MaxValue);
        }

        private static ulong Interleave(uint x, uint y)
            => (SpreadBits(x) << 1) | SpreadBits(y);

        private static ulong SpreadBits(uint value)
        {
            ulong bits = value;
            bits = (bits | (bits << 16)) & 0x0000FFFF0000FFFFUL;
            bits = (bits | (bits << 8)) & 0x00FF00FF00FF00FFUL;
            bits = (bits | (bits << 4)) & 0x0F0F0F0F0F0F0F0FUL;
            bits = (bits | (bits << 2)) & 0x3333333333333333UL;
            bits = (bits | (bits << 1)) & 0x5555555555555555UL;
            return bits;
        }

        private int LowerBound(ulong code)
        {
            int minimum = 0;
            int maximum = _vertices.Length;
            while (minimum < maximum)
            {
                int middle = minimum + ((maximum - minimum) / 2);
                if (_vertices[middle].Code < code)
                    minimum = middle + 1;
                else
                    maximum = middle;
            }
            return minimum;
        }

        private int UpperBound(ulong code)
        {
            int minimum = 0;
            int maximum = _vertices.Length;
            while (minimum < maximum)
            {
                int middle = minimum + ((maximum - minimum) / 2);
                if (_vertices[middle].Code <= code)
                    minimum = middle + 1;
                else
                    maximum = middle;
            }
            return minimum;
        }

        private readonly record struct MortonVertex(ulong Code, int Index);
    }

    private readonly record struct WorkVertex(double X, double Y, bool SourceToNext);

    private readonly record struct BridgeCandidate(double DistanceSquared, double X, double Y, int Index);

    private readonly record struct WorkTriangle(
        WorkVertex A,
        WorkVertex B,
        WorkVertex C,
        bool EdgeAB,
        bool EdgeBC,
        bool EdgeCA);

    private readonly record struct ClipVertex(WorkVertex Vertex, bool IncomingSource);
}

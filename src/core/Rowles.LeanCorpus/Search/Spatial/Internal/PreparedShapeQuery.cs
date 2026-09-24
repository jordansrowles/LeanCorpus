using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;

internal readonly record struct SpatialEnvelope(double MinX, double MinY, double MaxX, double MaxY)
{
    internal bool Intersects(SpatialEnvelope other)
        => MaxX >= other.MinX && MinX <= other.MaxX && MaxY >= other.MinY && MinY <= other.MaxY;

    internal bool Contains(SpatialEnvelope other)
        => MinX <= other.MinX && MinY <= other.MinY && MaxX >= other.MaxX && MaxY >= other.MaxY;
}

/// <summary>A query-local union of prepared primitives, rectangles and analytic circles.</summary>
internal sealed class PreparedShapeQuery
{
    private readonly List<SpatialEnvelope> _envelopes = [];
    private readonly List<SpatialEnvelope> _rectangles = [];
    private readonly List<GeoCircle> _geoCircles = [];
    private readonly List<XYCircle> _xyCircles = [];
    private SpatialComponentTree _components = new([]);

    internal List<ShapePrimitive> Primitives { get; } = [];
    internal SpatialFieldKind FieldKind { get; }
    internal bool IsGeo => FieldKind == SpatialFieldKind.GeoShape;
    internal IReadOnlyList<GeoCircle> GeoCircles => _geoCircles;
    internal IReadOnlyList<XYCircle> XYCircles => _xyCircles;
    internal bool HasComponents => _envelopes.Count != 0;

    private PreparedShapeQuery(SpatialFieldKind fieldKind) => FieldKind = fieldKind;

    internal static PreparedShapeQuery Prepare(GeoShapeQuery query)
    {
        var prepared = new PreparedShapeQuery(SpatialFieldKind.GeoShape);
        prepared._components = new SpatialComponentTree([prepared.AppendGeo(query.Geometry)]);
        return prepared;
    }

    internal static PreparedShapeQuery Prepare(XYShapeQuery query)
    {
        var prepared = new PreparedShapeQuery(SpatialFieldKind.XYShape);
        prepared._components = new SpatialComponentTree([prepared.AppendXY(query.Geometry)]);
        return prepared;
    }

    internal bool IsOutside(SpatialEnvelope cell)
        => _components.RelateEnvelope(cell) == SpatialEnvelopeRelation.Outside;

    internal bool IsCellInsideRectangle(SpatialEnvelope cell)
        => _components.RelateEnvelope(cell) == SpatialEnvelopeRelation.Inside;

    internal bool ContainsEnvelope(SpatialEnvelope cell)
        => IsCellInsideRectangle(cell);

    internal bool Intersects(ShapePrimitive primitive)
        => _components.Intersects(primitive);

    internal bool Contains(ShapeVertex point)
        => _components.ContainsPoint(point);

    internal SpatialWithinRelation WithinRelation(IReadOnlyList<ShapePrimitive> indexedValue)
        => _components.WithinRelation(indexedValue);

    internal bool Covers(ShapePrimitive primitive)
    {
        if (primitive.Kind == ShapePrimitiveKind.Point)
            return Contains(primitive.A);
        if (primitive.Kind == ShapePrimitiveKind.Line)
            return SpatialGeometryRelations.LineCoveredByQuery(primitive.A, primitive.B, this);
        return SpatialGeometryRelations.TriangleCoveredByQuery(primitive, this);
    }

    internal bool Covers(ShapePrimitive queryPrimitive, IReadOnlyList<ShapePrimitive> indexedValue)
    {
        if (queryPrimitive.Kind == ShapePrimitiveKind.Point)
            return SpatialGeometryRelations.ContainsPoint(indexedValue, queryPrimitive.A);
        if (queryPrimitive.Kind == ShapePrimitiveKind.Line)
            return SpatialGeometryRelations.LineCoveredByPrimitives(queryPrimitive.A, queryPrimitive.B, indexedValue, IsGeo);
        return SpatialGeometryRelations.TriangleCoveredByPrimitives(queryPrimitive, indexedValue, IsGeo);
    }

    internal bool CircleCoveredByValue(GeoCircle circle, IReadOnlyList<ShapePrimitive> indexedValue)
        => SpatialGeometryRelations.GeoCircleCoveredByPrimitives(circle, indexedValue);

    internal bool CircleCoveredByValue(XYCircle circle, IReadOnlyList<ShapePrimitive> indexedValue)
        => SpatialGeometryRelations.XYCircleCoveredByPrimitives(circle, indexedValue);

    private ISpatialComponent2D AppendGeo(IGeoGeometry geometry)
    {
        switch (geometry)
        {
            case GeoGeometryCollection collection:
                var children = new List<ISpatialComponent2D>(collection.Geometries.Count);
                foreach (IGeoGeometry component in collection.Geometries)
                    children.Add(AppendGeo(component));
                return new SpatialUnionComponent(children.ToArray());
            case GeoRectangle rectangle:
            {
                double south = GeoEncodingUtils.DecodeLat(GeoEncodingUtils.EncodeLat(rectangle.South));
                double north = GeoEncodingUtils.DecodeLat(GeoEncodingUtils.EncodeLat(rectangle.North));
                double west = GeoEncodingUtils.DecodeLon(GeoEncodingUtils.EncodeLon(rectangle.West));
                double east = GeoEncodingUtils.DecodeLon(GeoEncodingUtils.EncodeLon(rectangle.East));
                if (rectangle.CrossesDateline)
                {
                    AddRectangle(new SpatialEnvelope(west, south, 180, north));
                    AddRectangle(new SpatialEnvelope(-180, south, east, north));
                }
                else
                    AddRectangle(new SpatialEnvelope(west, south, east, north));
                List<ShapePrimitive> primitives = ShapeTessellator.PrepareGeo(rectangle, 0);
                Primitives.AddRange(primitives);
                return new SpatialRectangleComponent(primitives, isGeo: true,
                    rectangle.CrossesDateline
                        ? [new SpatialEnvelope(west, south, 180, north), new SpatialEnvelope(-180, south, east, north)]
                        : [new SpatialEnvelope(west, south, east, north)]);
            }
            case GeoCircle circle:
                _geoCircles.Add(circle);
                SpatialEnvelope[] circleEnvelopes = GeoCircleEnvelopes(circle).ToArray();
                foreach (SpatialEnvelope envelope in circleEnvelopes)
                    _envelopes.Add(envelope);
                return new SpatialGeoCircleComponent(circle, circleEnvelopes);
            default:
            {
                List<ShapePrimitive> primitives = ShapeTessellator.PrepareGeo(geometry, 0);
                AddPrimitives(primitives);
                return geometry switch
                {
                    GeoPoint => new SpatialPointComponent(primitives, isGeo: true),
                    GeoLineString => new SpatialLineComponent(primitives, isGeo: true),
                    GeoPolygon => new SpatialPolygonComponent(primitives, isGeo: true),
                    _ => throw new ArgumentException("Unsupported prepared geographic shape component.", nameof(geometry)),
                };
            }
        }
    }

    private ISpatialComponent2D AppendXY(IXYGeometry geometry)
    {
        switch (geometry)
        {
            case XYGeometryCollection collection:
                var children = new List<ISpatialComponent2D>(collection.Geometries.Count);
                foreach (IXYGeometry component in collection.Geometries)
                    children.Add(AppendXY(component));
                return new SpatialUnionComponent(children.ToArray());
            case XYRectangle rectangle:
            {
                var bounds = new SpatialEnvelope(rectangle.MinX, rectangle.MinY, rectangle.MaxX, rectangle.MaxY);
                AddRectangle(bounds);
                List<ShapePrimitive> primitives = ShapeTessellator.PrepareXY(rectangle, 0);
                Primitives.AddRange(primitives);
                return new SpatialRectangleComponent(primitives, isGeo: false, [bounds]);
            }
            case XYCircle circle:
            {
                _xyCircles.Add(circle);
                SpatialEnvelope bounds = new(
                    (double)circle.X - circle.Radius,
                    (double)circle.Y - circle.Radius,
                    (double)circle.X + circle.Radius,
                    (double)circle.Y + circle.Radius);
                _envelopes.Add(bounds);
                return new SpatialXYCircleComponent(circle, [bounds]);
            }
            default:
            {
                List<ShapePrimitive> primitives = ShapeTessellator.PrepareXY(geometry, 0);
                AddPrimitives(primitives);
                return geometry switch
                {
                    XYPoint => new SpatialPointComponent(primitives, isGeo: false),
                    XYLineString => new SpatialLineComponent(primitives, isGeo: false),
                    XYPolygon => new SpatialPolygonComponent(primitives, isGeo: false),
                    _ => throw new ArgumentException("Unsupported prepared Cartesian shape component.", nameof(geometry)),
                };
            }
        }
    }

    private void AddPrimitives(List<ShapePrimitive> primitives)
    {
        foreach (ShapePrimitive primitive in primitives)
        {
            Primitives.Add(primitive);
            _envelopes.Add(Envelope(primitive));
        }
    }

    private void AddRectangle(SpatialEnvelope rectangle)
    {
        _rectangles.Add(rectangle);
        _envelopes.Add(rectangle);
    }

    private static SpatialEnvelope Envelope(ShapePrimitive primitive)
    {
        double minX = Math.Min(primitive.A.X, Math.Min(primitive.B.X, primitive.C.X));
        double minY = Math.Min(primitive.A.Y, Math.Min(primitive.B.Y, primitive.C.Y));
        double maxX = Math.Max(primitive.A.X, Math.Max(primitive.B.X, primitive.C.X));
        double maxY = Math.Max(primitive.A.Y, Math.Max(primitive.B.Y, primitive.C.Y));
        return new SpatialEnvelope(minX, minY, maxX, maxY);
    }

    private static IEnumerable<SpatialEnvelope> GeoCircleEnvelopes(GeoCircle circle)
    {
        const double earthRadiusMetres = 6_371_000d;
        const double radiansToDegrees = 180d / Math.PI;
        double angularRadius = circle.RadiusMetres / earthRadiusMetres;
        if (angularRadius >= Math.PI)
        {
            yield return new SpatialEnvelope(-180, -90, 180, 90);
            yield break;
        }

        double angularLatitude = angularRadius * radiansToDegrees;
        double minLatitude = Math.Max(-90, circle.Latitude - angularLatitude);
        double maxLatitude = Math.Min(90, circle.Latitude + angularLatitude);
        if (minLatitude <= -90 || maxLatitude >= 90)
        {
            yield return new SpatialEnvelope(-180, minLatitude, 180, maxLatitude);
            yield break;
        }

        double ratio = Math.Sin(angularRadius) / Math.Cos(circle.Latitude / radiansToDegrees);
        double angularLongitude = Math.Asin(Math.Clamp(ratio, -1, 1)) * radiansToDegrees;
        double west = GeoEncodingUtils.NormaliseLongitude(circle.Longitude - angularLongitude);
        double east = GeoEncodingUtils.NormaliseLongitude(circle.Longitude + angularLongitude);
        if (west > east || (circle.Longitude - angularLongitude) < -180 || (circle.Longitude + angularLongitude) > 180)
        {
            yield return new SpatialEnvelope(west, minLatitude, 180, maxLatitude);
            yield return new SpatialEnvelope(-180, minLatitude, east, maxLatitude);
        }
        else
            yield return new SpatialEnvelope(west, minLatitude, east, maxLatitude);
    }
}

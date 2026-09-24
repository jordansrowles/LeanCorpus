using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class ShapeTessellatorTests
{
    [Fact(DisplayName = "Shape tessellation preserves triangle, rectangle and concave polygon area")]
    public void PrepareXY_PolygonsPreserveArea()
    {
        List<ShapePrimitive> triangle = ShapeTessellator.PrepareXY(
            new XYPolygon([new XYPoint(0, 0), new XYPoint(10, 0), new XYPoint(5, 8)]), 0);
        List<ShapePrimitive> rectangle = ShapeTessellator.PrepareXY(new XYRectangle(0, 0, 10, 10), 0);
        List<ShapePrimitive> concave = ShapeTessellator.PrepareXY(
            new XYPolygon(
            [
                new XYPoint(0, 0), new XYPoint(6, 0), new XYPoint(6, 2),
                new XYPoint(2, 2), new XYPoint(2, 6), new XYPoint(0, 6),
            ]), 0);

        Assert.Single(triangle);
        Assert.Equal(40, Area(triangle), precision: 4);
        Assert.Equal(2, rectangle.Count);
        Assert.Equal(100, Area(rectangle), precision: 4);
        Assert.Equal(4, concave.Count);
        Assert.Equal(20, Area(concave), precision: 4);
    }

    [Fact(DisplayName = "Shape tessellation covers holes and retains source-edge provenance")]
    public void PrepareXY_PolygonWithHoleLeavesHoleAndBridgeEdgesInternal()
    {
        var polygon = new XYPolygon(
            [new XYPoint(0, 0), new XYPoint(10, 0), new XYPoint(10, 10), new XYPoint(0, 10)],
            [[new XYPoint(3, 3), new XYPoint(3, 7), new XYPoint(7, 7), new XYPoint(7, 3)]]);

        List<ShapePrimitive> primitives = ShapeTessellator.PrepareXY(polygon, 0);

        Assert.Equal(84, Area(primitives), precision: 4);
        Assert.Equal(8, primitives.Sum(static primitive =>
            (primitive.EdgeAB ? 1 : 0) + (primitive.EdgeBC ? 1 : 0) + (primitive.EdgeCA ? 1 : 0)));
        Assert.All(primitives, static primitive => Assert.Equal(ShapePrimitiveKind.Triangle, primitive.Kind));
    }

    [Fact(DisplayName = "Shape tessellation covers strongly concave and multiple-hole polygons")]
    public void PrepareXY_StrongConcavityAndMultipleHolesPreserveSolidArea()
    {
        var stronglyConcave = new XYPolygon(
        [
            new XYPoint(0, 0), new XYPoint(10, 0), new XYPoint(10, 2), new XYPoint(2, 2),
            new XYPoint(2, 8), new XYPoint(10, 8), new XYPoint(10, 10), new XYPoint(0, 10),
        ]);
        var multipleHoles = new XYPolygon(
            [new XYPoint(0, 0), new XYPoint(20, 0), new XYPoint(20, 20), new XYPoint(0, 20)],
            [
                [new XYPoint(2, 2), new XYPoint(2, 4), new XYPoint(4, 4), new XYPoint(4, 2)],
                [new XYPoint(10, 10), new XYPoint(10, 14), new XYPoint(14, 14), new XYPoint(14, 10)],
            ]);

        List<ShapePrimitive> concave = ShapeTessellator.PrepareXY(stronglyConcave, 0);
        List<ShapePrimitive> holed = ShapeTessellator.PrepareXY(multipleHoles, 0);

        Assert.Equal(52, Area(concave), precision: 4);
        Assert.Equal(380, Area(holed), precision: 4);
        Assert.Equal(12, holed.Sum(static primitive =>
            (primitive.EdgeAB ? 1 : 0) + (primitive.EdgeBC ? 1 : 0) + (primitive.EdgeCA ? 1 : 0)));
        Assert.Equal(holed, ShapeTessellator.PrepareXY(multipleHoles, 0));
    }

    [Fact(DisplayName = "Large concave rings use bounded z-order ear clipping deterministically")]
    public void PrepareXY_LargeConcaveRingPreservesAreaAndSourceEdges()
    {
        XYPoint[] vertices = Enumerable.Range(0, 256)
            .Select(index =>
            {
                double angle = 2 * Math.PI * index / 256;
                float radius = (index & 1) == 0 ? 100 : 65;
                return new XYPoint((float)(Math.Cos(angle) * radius), (float)(Math.Sin(angle) * radius));
            })
            .ToArray();
        var polygon = new XYPolygon(vertices);

        List<ShapePrimitive> first = ShapeTessellator.PrepareXY(polygon, 3);
        List<ShapePrimitive> second = ShapeTessellator.PrepareXY(polygon, 3);

        Assert.Equal(vertices.Length - 2, first.Count);
        Assert.Equal(SignedArea(vertices), Area(first), precision: 3);
        Assert.Equal(vertices.Length, first.Sum(static primitive =>
            (primitive.EdgeAB ? 1 : 0) + (primitive.EdgeBC ? 1 : 0) + (primitive.EdgeCA ? 1 : 0)));
        Assert.Equal(first, second);
    }

    [Fact(DisplayName = "Shape tessellation retains narrow valid polygons and rejects touching holes")]
    public void PrepareXY_NarrowPolygonAndHoleTouchingFollowTopologyContract()
    {
        var narrow = new XYPolygon(
            [new XYPoint(0, 0), new XYPoint(10, 0), new XYPoint(10, 0.00001f)]);
        List<ShapePrimitive> narrowPrimitives = ShapeTessellator.PrepareXY(narrow, 0);

        Assert.Single(narrowPrimitives);
        Assert.True(Area(narrowPrimitives) > 0);
        Assert.Throws<ArgumentException>(() => new XYPolygon(
            [new XYPoint(0, 0), new XYPoint(10, 0), new XYPoint(10, 10), new XYPoint(0, 10)],
            [[new XYPoint(0, 2), new XYPoint(2, 2), new XYPoint(2, 4), new XYPoint(0, 4)]]));
    }

    [Fact(DisplayName = "Shape tessellation removes quantised collinear vertices deterministically")]
    public void PrepareXY_CollinearShellVerticesProduceStablePrimitives()
    {
        var polygon = new XYPolygon(
        [
            new XYPoint(0, 0), new XYPoint(5, 0), new XYPoint(10, 0),
            new XYPoint(10, 10), new XYPoint(0, 10),
        ]);

        List<ShapePrimitive> first = ShapeTessellator.PrepareXY(polygon, 7);
        List<ShapePrimitive> second = ShapeTessellator.PrepareXY(polygon, 7);

        Assert.Equal(2, first.Count);
        Assert.Equal(100, Area(first), precision: 4);
        Assert.Equal(first, second);
        Assert.All(first, static primitive => Assert.Equal((uint)7, primitive.ValueOrdinal));
    }

    [Fact(DisplayName = "Geo shape preparation splits Date Line lines, polygons and rectangles")]
    public void PrepareGeo_CrossingGeometryNeverEmitsWorldSpanningPrimitives()
    {
        var line = new GeoLineString([new GeoPoint(0, 170), new GeoPoint(0, -170)]);
        var polygon = new GeoPolygon(
        [
            new GeoPoint(-10, 170), new GeoPoint(-10, -170),
            new GeoPoint(10, -170), new GeoPoint(10, 170),
        ]);
        var rectangle = new GeoRectangle(-10, 170, 10, -170);

        List<ShapePrimitive> linePrimitives = ShapeTessellator.PrepareGeo(line, 0);
        List<ShapePrimitive> polygonPrimitives = ShapeTessellator.PrepareGeo(polygon, 0);
        List<ShapePrimitive> rectanglePrimitives = ShapeTessellator.PrepareGeo(rectangle, 0);

        Assert.Equal(2, linePrimitives.Count);
        Assert.NotEmpty(polygonPrimitives);
        Assert.NotEmpty(rectanglePrimitives);
        AssertNoWorldSpanningLongitude(linePrimitives);
        AssertNoWorldSpanningLongitude(polygonPrimitives);
        AssertNoWorldSpanningLongitude(rectanglePrimitives);
    }

    [Fact(DisplayName = "Geo Date Line polygon holes remain uncovered after deterministic seam splitting")]
    public void PrepareGeo_CrossingPolygonWithHolePreservesAreaAndBoundaryFlags()
    {
        var polygon = new GeoPolygon(
            [
                new GeoPoint(-10, 170), new GeoPoint(-10, -170),
                new GeoPoint(10, -170), new GeoPoint(10, 170),
            ],
            [[
                new GeoPoint(-2, 175), new GeoPoint(-2, -175),
                new GeoPoint(2, -175), new GeoPoint(2, 175),
            ]]);

        List<ShapePrimitive> primitives = ShapeTessellator.PrepareGeo(polygon, 0);

        Assert.Equal(360, Area(primitives), precision: 3);
        AssertNoWorldSpanningLongitude(primitives);
        Assert.All(primitives, static primitive => Assert.Equal(ShapePrimitiveKind.Triangle, primitive.Kind));
        Assert.True(primitives.Sum(static primitive =>
            (primitive.EdgeAB ? 1 : 0) + (primitive.EdgeBC ? 1 : 0) + (primitive.EdgeCA ? 1 : 0)) >= 8);
    }

    [Fact(DisplayName = "Shape geometry collections retain one field-value ordinal")]
    public void PrepareXY_CollectionComponentsShareValueOrdinal()
    {
        var collection = new XYGeometryCollection(
        [
            new XYPoint(1, 2),
            new XYLineString([new XYPoint(3, 4), new XYPoint(5, 6)]),
        ]);

        List<ShapePrimitive> primitives = ShapeTessellator.PrepareXY(collection, 9);

        Assert.Equal(2, primitives.Count);
        Assert.All(primitives, static primitive => Assert.Equal((uint)9, primitive.ValueOrdinal));
        Assert.Contains(primitives, static primitive => primitive.Kind == ShapePrimitiveKind.Point);
        Assert.Contains(primitives, static primitive => primitive.Kind == ShapePrimitiveKind.Line);
    }

    [Fact(DisplayName = "Shape fields reject circles and enable Shape DocValues by default")]
    public void ShapeFields_RejectQueryOnlyCirclesAndUseFinalFieldSettings()
    {
        Assert.Throws<ArgumentException>(() => new LatLonShapeField("geo", new GeoCircle(0, 0, 100)));
        Assert.Throws<ArgumentException>(() => new XYShapeField("xy", new XYCircle(0, 0, 1)));
        Assert.Throws<ArgumentException>(() => new LatLonShapeField(
            "geo",
            new GeoGeometryCollection([new GeoPoint(0, 0), new GeoCircle(0, 0, 1)])));

        var field = new XYShapeField("xy", new XYRectangle(0, 0, 1, 1));
        Assert.Equal(FieldType.Binary, field.FieldType);
        Assert.True(field.IsIndexed);
        Assert.False(field.IsStored);
        Assert.True(field.StoreDocValues);
        Assert.False(new XYShapeField("xy-no-dv", new XYRectangle(0, 0, 1, 1), storeDocValues: false).StoreDocValues);
        Assert.Equal(FieldIndexOptions.DocsOnly, field.IndexOptions);
    }

    private static double Area(IReadOnlyList<ShapePrimitive> primitives)
    {
        double area = 0;
        foreach (ShapePrimitive primitive in primitives)
            if (primitive.Kind == ShapePrimitiveKind.Triangle)
                area += Math.Abs((primitive.B.X - primitive.A.X) * (primitive.C.Y - primitive.A.Y)
                    - (primitive.B.Y - primitive.A.Y) * (primitive.C.X - primitive.A.X)) / 2;
        return area;
    }

    private static double SignedArea(IReadOnlyList<XYPoint> vertices)
    {
        double area = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            XYPoint first = vertices[i];
            XYPoint second = vertices[(i + 1) % vertices.Count];
            area += (double)first.X * second.Y - (double)second.X * first.Y;
        }
        return Math.Abs(area) / 2;
    }

    private static void AssertNoWorldSpanningLongitude(IReadOnlyList<ShapePrimitive> primitives)
    {
        foreach (ShapePrimitive primitive in primitives)
        {
            double min = Math.Min(primitive.A.X, Math.Min(primitive.B.X, primitive.C.X));
            double max = Math.Max(primitive.A.X, Math.Max(primitive.B.X, primitive.C.X));
            Assert.InRange(min, -180, 180);
            Assert.InRange(max, -180, 180);
            Assert.True(max - min <= 20.001, $"Primitive longitude span was {max - min} degrees.");
        }
    }
}

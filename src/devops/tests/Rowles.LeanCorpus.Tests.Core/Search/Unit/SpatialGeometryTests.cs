namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class SpatialGeometryTests
{
    [Fact(DisplayName = "Geometry accepts finite boundaries and rejects non-finite coordinates")]
    public void Geometry_ValidatesFiniteBoundaries()
    {
        Assert.Equal(new GeoPoint(-90, -180), new GeoPoint(-90, -180));
        Assert.Equal(new GeoPoint(90, 180), new GeoPoint(90, 180));
        Assert.Equal(new XYPoint(float.MinValue, float.MaxValue), new XYPoint(float.MinValue, float.MaxValue));

        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPoint(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPoint(0, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XYPoint(float.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XYPoint(0, float.NegativeInfinity));
    }

    [Fact(DisplayName = "Geo geometry validates coordinates and canonicalises rings")]
    public void GeoGeometry_ValidatesAndCanonicalises()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPoint(91, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPoint(0, 181));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoCircle(0, 0, -1));

        var shell = new GeoPolygon([
            new GeoPoint(0, 0),
            new GeoPoint(0, 10),
            new GeoPoint(10, 10),
            new GeoPoint(10, 0),
            new GeoPoint(0, 0),
        ]);

        Assert.Equal(shell.Shell[0], shell.Shell[^1]);
        Assert.Equal(4, shell.Shell.Count - 1);
        Assert.True(SignedArea(shell.Shell) > 0);
        Assert.Equal(shell, new GeoPolygon([
            new GeoPoint(0, 0),
            new GeoPoint(0, 10),
            new GeoPoint(10, 10),
            new GeoPoint(10, 0)]));
    }

    [Fact(DisplayName = "Geo and XY rectangles preserve dateline, pole and boundary semantics")]
    public void Rectangles_PreserveBoundarySemantics()
    {
        var crossing = new GeoRectangle(-90, 170, 90, -170);
        Assert.True(crossing.CrossesDateline);
        Assert.False(new GeoRectangle(-90, -180, 90, 180).CrossesDateline);

        Assert.Equal(0, new GeoCircle(0, 0, 0).RadiusMetres);
        Assert.Equal(0, new XYCircle(0, 0, 0).Radius);
        Assert.Equal(new XYRectangle(-2, -1, 3, 4), new XYRectangle(-2, -1, 3, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoRectangle(1, 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XYRectangle(1, 0, 0, 1));
    }

    [Fact(DisplayName = "Line strings collapse duplicates and reject encoded degeneracy")]
    public void LineStrings_CollapseDuplicatesAndRejectDegeneracy()
    {
        var line = new GeoLineString([
            new GeoPoint(0, 0),
            new GeoPoint(0, 0),
            new GeoPoint(1, 1),
            new GeoPoint(1, 1),
        ]);
        Assert.Equal(2, line.Points.Count);
        Assert.Throws<ArgumentException>(() => new GeoLineString([new GeoPoint(0, 0)]));
        Assert.Throws<ArgumentException>(() => new GeoLineString([
            new GeoPoint(0, 0),
            new GeoPoint(1e-12, 1e-12),
        ]));

        var xyLine = new XYLineString([new XYPoint(0, 0), new XYPoint(0, 0), new XYPoint(1, 1)]);
        Assert.Equal(2, xyLine.Points.Count);

        var crossing = new GeoLineString([new GeoPoint(0, 179), new GeoPoint(1, -179)]);
        Assert.Equal(crossing, new GeoLineString(crossing.Points));
    }

    [Fact(DisplayName = "Geo geometry inserts dateline seam points")]
    public void GeoGeometry_DatelineRingContainsSeamPoints()
    {
        var polygon = new GeoPolygon([
            new GeoPoint(10, 179),
            new GeoPoint(20, -179),
            new GeoPoint(0, -179),
        ]);

        Assert.Contains(polygon.Shell, point => point.Longitude == 180);
        Assert.Contains(polygon.Shell, point => point.Longitude == -180);
        Assert.Equal(polygon.Shell[0], polygon.Shell[^1]);
        Assert.Equal(polygon, new GeoPolygon(polygon.Shell));
    }

    [Fact(DisplayName = "Geo polygon rejects self intersection and invalid holes")]
    public void GeoPolygon_RejectsInvalidTopology()
    {
        Assert.Throws<ArgumentException>(() => new GeoPolygon([
            new GeoPoint(0, 0),
            new GeoPoint(10, 10),
            new GeoPoint(0, 10),
            new GeoPoint(10, 0),
        ]));

        Assert.Throws<ArgumentException>(() => new GeoPolygon(
            [new GeoPoint(0, 0), new GeoPoint(0, 10), new GeoPoint(10, 10), new GeoPoint(10, 0)],
            [[new GeoPoint(20, 20), new GeoPoint(20, 21), new GeoPoint(21, 21)]]));

        Assert.Throws<ArgumentException>(() => new GeoPolygon([
            new GeoPoint(0, 0),
            new GeoPoint(1, 1),
            new GeoPoint(2, 2),
        ]));

        Assert.Throws<ArgumentException>(() => new GeoPolygon(
            [new GeoPoint(0, 0), new GeoPoint(0, 10), new GeoPoint(10, 10), new GeoPoint(10, 0)],
            [[new GeoPoint(1, 1), new GeoPoint(1, 9), new GeoPoint(9, 9), new GeoPoint(9, 1)],
             [new GeoPoint(5, 5), new GeoPoint(5, 8), new GeoPoint(8, 8), new GeoPoint(8, 5)]]));

        var polygon = new GeoPolygon(
            [new GeoPoint(0, 0), new GeoPoint(0, 20), new GeoPoint(20, 20), new GeoPoint(20, 0)],
            [[new GeoPoint(2, 2), new GeoPoint(2, 5), new GeoPoint(5, 5), new GeoPoint(5, 2)],
             [new GeoPoint(10, 10), new GeoPoint(10, 14), new GeoPoint(14, 14), new GeoPoint(14, 10)]]);
        Assert.Equal(2, polygon.Holes.Count);
        Assert.True(SignedArea(polygon.Holes[0]) < 0);
        Assert.True(SignedArea(polygon.Holes[1]) < 0);
    }

    [Fact(DisplayName = "Geo polygon validates dateline holes in one common world")]
    public void GeoPolygon_UsesCommonUnwrappedFrameForHoles()
    {
        var polygon = new GeoPolygon(
            [new GeoPoint(0, 170), new GeoPoint(0, -170), new GeoPoint(20, -170), new GeoPoint(20, 170)],
            [[new GeoPoint(5, -175), new GeoPoint(5, 175), new GeoPoint(15, 175), new GeoPoint(15, -175)]]);

        Assert.Single(polygon.Holes);
        Assert.Throws<ArgumentException>(() => new GeoPolygon(
            [new GeoPoint(0, 170), new GeoPoint(0, -170), new GeoPoint(20, -170), new GeoPoint(20, 170)],
            [[new GeoPoint(5, -165), new GeoPoint(5, -155), new GeoPoint(15, -155), new GeoPoint(15, -165)]]));
    }

    [Fact(DisplayName = "Geo polygon rejects world winding and quantised topology changes")]
    public void GeoPolygon_RejectsWorldWrapsAndQuantisedInvalidTopology()
    {
        Assert.Throws<ArgumentException>(() => new GeoPolygon([
            new GeoPoint(0, 0), new GeoPoint(10, 120), new GeoPoint(0, -120)]));

        const double tiny = 1e-12;
        Assert.Throws<ArgumentException>(() => new GeoPolygon(
            [new GeoPoint(0, 0), new GeoPoint(0, 1), new GeoPoint(1, 1), new GeoPoint(1, 0)],
            [[new GeoPoint(tiny, tiny), new GeoPoint(tiny, 0.25), new GeoPoint(0.25, 0.25), new GeoPoint(0.25, tiny)]]));
    }

    [Fact(DisplayName = "XY geometry validates, closes rings and copies input")]
    public void XYGeometry_ValidatesAndCopies()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new XYPoint(float.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XYCircle(0, 0, -1));

        var input = new List<XYPoint>
        {
            new(0, 0), new(10, 0), new(10, 10), new(0, 10),
        };
        var polygon = new XYPolygon(input);
        input[0] = new XYPoint(100, 100);

        Assert.Equal(polygon.Shell[0], polygon.Shell[^1]);
        Assert.Contains(new XYPoint(0, 0), polygon.Shell);
        Assert.True(SignedArea(polygon.Shell) > 0);
    }

    [Fact(DisplayName = "Geometry collections are copied, flat and immutable")]
    public void Collections_CopyAndRejectInvalidNesting()
    {
        var geometries = new List<IGeoGeometry> { new GeoPoint(1, 2), new GeoCircle(3, 4, 5) };
        var collection = new GeoGeometryCollection(geometries);
        geometries.Clear();
        Assert.Equal(2, collection.Geometries.Count);
        Assert.Equal(collection, new GeoGeometryCollection([
            new GeoPoint(1, 2), new GeoCircle(3, 4, 5)]));
        Assert.Throws<NotSupportedException>(() => ((IList<IGeoGeometry>)collection.Geometries)[0] = new GeoPoint(0, 0));
        Assert.Throws<ArgumentException>(() => new GeoGeometryCollection([]));
        Assert.Throws<ArgumentException>(() => new GeoGeometryCollection([
            new GeoGeometryCollection([new GeoPoint(1, 2)])]));

        var xy = new XYGeometryCollection([new XYPoint(1, 2), new XYCircle(3, 4, 5)]);
        Assert.Equal(2, xy.Geometries.Count);
        Assert.Throws<ArgumentException>(() => new XYGeometryCollection([]));
        Assert.Throws<ArgumentException>(() => new XYGeometryCollection([
            new XYGeometryCollection([new XYPoint(1, 2)])]));
    }

    [Fact(DisplayName = "Geometry collections reject custom marker implementations")]
    public void Collections_RejectCustomGeometryImplementations()
    {
        ArgumentException geoError = Assert.Throws<ArgumentException>(
            () => new GeoGeometryCollection([new CustomGeoGeometry()]));
        Assert.Contains("only built-in", geoError.Message);

        ArgumentException xyError = Assert.Throws<ArgumentException>(
            () => new XYGeometryCollection([new CustomXyGeometry()]));
        Assert.Contains("only built-in", xyError.Message);

        Assert.Throws<ArgumentException>(() => new GeoGeometryCollection([null!]));
        Assert.Throws<ArgumentException>(() => new XYGeometryCollection([null!]));
    }

    [Fact(DisplayName = "Geometry equality and hashes are value based")]
    public void Geometry_EqualityAndHashAreValueBased()
    {
        var first = new GeoLineString([new GeoPoint(0, 0), new GeoPoint(1, 1)]);
        var second = new GeoLineString([new GeoPoint(0, 0), new GeoPoint(1, 1)]);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, new GeoLineString([new GeoPoint(0, 0), new GeoPoint(2, 2)]));
    }

    [Fact(DisplayName = "Coordinate encoders preserve sortable ordering")]
    public void EncodingUtils_PreserveSortableOrdering()
    {
        Assert.True(GeoEncodingUtils.EncodeLatFloor(-10) < GeoEncodingUtils.EncodeLatFloor(10));
        Assert.True(GeoEncodingUtils.EncodeLonCeil(-10) < GeoEncodingUtils.EncodeLonCeil(10));

        Span<byte> negative = stackalloc byte[4];
        Span<byte> positive = stackalloc byte[4];
        XYEncodingUtils.Encode(-1, negative);
        XYEncodingUtils.Encode(1, positive);
        Assert.True(negative.SequenceCompareTo(positive) < 0);
        Assert.Equal(-1, XYEncodingUtils.Decode(negative));
        Assert.Equal(1, XYEncodingUtils.Decode(positive));
    }

    [Fact(DisplayName = "Geo encoding covers endpoints and rounds query bounds outwards")]
    public void GeoEncoding_CoversEndpointsAndOutwardRounding()
    {
        Assert.Equal(-179, GeoEncodingUtils.NormaliseLongitude(181));
        Assert.Equal(int.MinValue, GeoEncodingUtils.EncodeLat(-90));
        Assert.Equal(int.MaxValue, GeoEncodingUtils.EncodeLat(90));
        Assert.Equal(int.MinValue, GeoEncodingUtils.EncodeLon(-180));
        Assert.Equal(int.MaxValue, GeoEncodingUtils.EncodeLon(180));

        const double latitude = 12.3456789;
        const double longitude = -123.456789;
        int latitudeFloor = GeoEncodingUtils.EncodeLatFloor(latitude);
        int latitudeCeil = GeoEncodingUtils.EncodeLatCeil(latitude);
        int longitudeFloor = GeoEncodingUtils.EncodeLonFloor(longitude);
        int longitudeCeil = GeoEncodingUtils.EncodeLonCeil(longitude);
        Assert.True(GeoEncodingUtils.DecodeLat(latitudeFloor) <= latitude);
        Assert.True(GeoEncodingUtils.DecodeLat(latitudeCeil) >= latitude);
        Assert.True(GeoEncodingUtils.DecodeLon(longitudeFloor) <= longitude);
        Assert.True(GeoEncodingUtils.DecodeLon(longitudeCeil) >= longitude);
        Assert.Equal(0, GeoEncodingUtils.HaversineDistance(0, 0, 0, 0));
        Assert.InRange(GeoEncodingUtils.HaversineDistance(0, 179, 0, -179), 220_000, 225_000);
        Assert.InRange(GeoEncodingUtils.HaversineDistance(0, 0, 0, 180), 20_000_000, 20_020_000);
        Assert.InRange(GeoEncodingUtils.HaversineDistance(0, 0, 0.0001, 179.9999), 20_000_000, 20_020_000);
    }

    [Fact(DisplayName = "XY geometry provides validated Cartesian primitives")]
    public void XYGeometry_ProvidesValidatedPrimitives()
    {
        Assert.Equal(new XYPoint(1, 2), new XYPoint(1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XYCircle(0, 0, float.NaN));
        Assert.Throws<ArgumentException>(() => new XYLineString([new XYPoint(0, 0)]));
        Assert.Throws<ArgumentException>(() => new XYPolygon([
            new XYPoint(0, 0), new XYPoint(1, 1), new XYPoint(2, 2)]));
    }

    [Fact(DisplayName = "XY topology remains finite at the float range")]
    public void XYGeometry_UsesDoubleTopologyMaths()
    {
        const float maximum = float.MaxValue;
        var polygon = new XYPolygon([
            new XYPoint(-maximum, -maximum),
            new XYPoint(maximum, -maximum),
            new XYPoint(maximum, maximum),
            new XYPoint(-maximum, maximum)]);

        Assert.Equal(5, polygon.Shell.Count);
    }

    [Fact(DisplayName = "XY sortable encoding canonicalises signed zero")]
    public void XYEncoding_CanonicalisesSignedZero()
    {
        Span<byte> negative = stackalloc byte[4];
        Span<byte> positive = stackalloc byte[4];
        XYEncodingUtils.Encode(-0f, negative);
        XYEncodingUtils.Encode(+0f, positive);

        Assert.Equal(positive.ToArray(), negative.ToArray());
        Assert.Equal(0f, XYEncodingUtils.Decode(negative));
        Assert.Equal(0, BitConverter.SingleToInt32Bits(XYEncodingUtils.Decode(negative)));
    }

    private static double SignedArea(IReadOnlyList<GeoPoint> ring)
    {
        double area = 0;
        for (int i = 0; i < ring.Count - 1; i++)
            area += ring[i].Longitude * ring[i + 1].Latitude - ring[i + 1].Longitude * ring[i].Latitude;
        return area / 2;
    }

    private sealed class CustomGeoGeometry : IGeoGeometry { }

    private sealed class CustomXyGeometry : IXYGeometry { }

    private static float SignedArea(IReadOnlyList<XYPoint> ring)
    {
        float area = 0;
        for (int i = 0; i < ring.Count - 1; i++)
            area += ring[i].X * ring[i + 1].Y - ring[i + 1].X * ring[i].Y;
        return area / 2;
    }
}

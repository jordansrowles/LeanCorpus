namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class SpatialGeometryTests
{
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

    private static double SignedArea(IReadOnlyList<GeoPoint> ring)
    {
        double area = 0;
        for (int i = 0; i < ring.Count - 1; i++)
            area += ring[i].Longitude * ring[i + 1].Latitude - ring[i + 1].Longitude * ring[i].Latitude;
        return area / 2;
    }

    private static float SignedArea(IReadOnlyList<XYPoint> ring)
    {
        float area = 0;
        for (int i = 0; i < ring.Count - 1; i++)
            area += ring[i].X * ring[i + 1].Y - ring[i + 1].X * ring[i].Y;
        return area / 2;
    }
}

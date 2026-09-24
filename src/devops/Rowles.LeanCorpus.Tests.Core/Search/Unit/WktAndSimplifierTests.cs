using System.Globalization;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Tests.Core.Search.Spatial;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class WktAndSimplifierTests
{
    [Fact(DisplayName = "WKT parses case-insensitive Geo and XY geometry families and flattens collections")]
    public void WktReader_ParsesSupportedGeometryFamilies()
    {
        IGeoGeometry geoMulti = WktReader.ParseGeo(" mUlTiPoInT ( (179 10), (-179 11) ) ");
        GeoGeometryCollection geoPoints = Assert.IsType<GeoGeometryCollection>(geoMulti);
        Assert.Equal(new GeoPoint(10, 179), Assert.IsType<GeoPoint>(geoPoints.Geometries[0]));
        Assert.Equal(new GeoPoint(11, -179), Assert.IsType<GeoPoint>(geoPoints.Geometries[1]));

        IGeoGeometry nested = WktReader.ParseGeo(
            "GEOMETRYCOLLECTION (POINT (1 2), GEOMETRYCOLLECTION (LINESTRING (0 0, 1 1), MULTIPOINT (3 4, 5 6)))");
        GeoGeometryCollection flat = Assert.IsType<GeoGeometryCollection>(nested);
        Assert.Equal(4, flat.Geometries.Count);
        Assert.IsType<GeoPoint>(flat.Geometries[0]);
        Assert.IsType<GeoLineString>(flat.Geometries[1]);
        Assert.IsType<GeoPoint>(flat.Geometries[2]);
        Assert.IsType<GeoPoint>(flat.Geometries[3]);

        IXYGeometry xyMultiLine = WktReader.ParseXY("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))");
        XYGeometryCollection xyLines = Assert.IsType<XYGeometryCollection>(xyMultiLine);
        Assert.Equal(2, xyLines.Geometries.Count);
        Assert.All(xyLines.Geometries, static geometry => Assert.IsType<XYLineString>(geometry));
    }

    [Fact(DisplayName = "WKT supports every multi geometry, exponent numbers and ASCII whitespace")]
    public void WktReader_ParsesMultiFamiliesAndLegalAsciiWhitespace()
    {
        GeoPoint point = Assert.IsType<GeoPoint>(WktReader.ParseGeo("\f pOiNt\t(\t+1.25e2\r\n-2.5E+1\v)"));
        Assert.Equal(new GeoPoint(-25, 125), point);

        GeoGeometryCollection geoLines = Assert.IsType<GeoGeometryCollection>(WktReader.ParseGeo(
            "MULTILINESTRING ((0 0, 1 0), (2 2, 3 3))"));
        Assert.Equal(2, geoLines.Geometries.Count);
        Assert.All(geoLines.Geometries, static geometry => Assert.IsType<GeoLineString>(geometry));

        GeoGeometryCollection geoPolygons = Assert.IsType<GeoGeometryCollection>(WktReader.ParseGeo(
            "MULTIPOLYGON (((0 0, 2 0, 2 2, 0 2, 0 0)), ((10 10, 12 10, 12 12, 10 12, 10 10)))"));
        Assert.Equal(2, geoPolygons.Geometries.Count);
        Assert.All(geoPolygons.Geometries, static geometry => Assert.IsType<GeoPolygon>(geometry));

        XYGeometryCollection xyPoints = Assert.IsType<XYGeometryCollection>(WktReader.ParseXY(
            "MULTIPOINT (1 2, (3 4))"));
        Assert.Equal(2, xyPoints.Geometries.Count);
        Assert.All(xyPoints.Geometries, static geometry => Assert.IsType<XYPoint>(geometry));

        XYGeometryCollection xyPolygons = Assert.IsType<XYGeometryCollection>(WktReader.ParseXY(
            "MULTIPOLYGON (((0 0, 2 0, 2 2, 0 2, 0 0)), ((10 10, 12 10, 12 12, 10 12, 10 10)))"));
        Assert.Equal(2, xyPolygons.Geometries.Count);
        Assert.All(xyPolygons.Geometries, static geometry => Assert.IsType<XYPolygon>(geometry));
    }

    [Fact(DisplayName = "WKT writer emits deterministic round-trip text and rectangles as polygons")]
    public void WktWriter_WritesCanonicalInvariantGeometry()
    {
        GeoPolygon polygon = Assert.IsType<GeoPolygon>(WktReader.ParseGeo(
            "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0), (1 1, 1 2, 2 2, 2 1, 1 1))"));
        string polygonText = WktWriter.Write(polygon);
        Assert.StartsWith("POLYGON ((", polygonText, StringComparison.Ordinal);
        Assert.Equal(polygon, WktReader.ParseGeo(polygonText));

        GeoGeometryCollection collection = Assert.IsType<GeoGeometryCollection>(WktReader.ParseGeo(
            "GEOMETRYCOLLECTION (POINT (1.25 -2), LINESTRING (0 0, 1 1))"));
        string collectionText = WktWriter.Write(collection);
        Assert.Equal(collection, WktReader.ParseGeo(collectionText));

        string rectangleText = WktWriter.Write(new GeoRectangle(-1, 179, 1, -179));
        Assert.StartsWith("POLYGON (", rectangleText, StringComparison.Ordinal);
        Assert.IsType<GeoPolygon>(WktReader.ParseGeo(rectangleText));

        string xyRectangleText = WktWriter.Write(new XYRectangle(-2, -1, 3, 4));
        Assert.StartsWith("POLYGON (", xyRectangleText, StringComparison.Ordinal);
        Assert.IsType<XYPolygon>(WktReader.ParseXY(xyRectangleText));
    }

    [Fact(DisplayName = "WKT numbers remain invariant under a comma-decimal culture")]
    public void WktReaderAndWriter_AreCultureInvariant()
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            GeoPoint point = Assert.IsType<GeoPoint>(WktReader.ParseGeo("POINT (12.5 -3.75)"));
            Assert.Equal("POINT (12.5 -3.75)", WktWriter.Write(point));
            XYPoint xyPoint = Assert.IsType<XYPoint>(WktReader.ParseXY("POINT (1.25 2.5)"));
            Assert.Equal("POINT (1.25 2.5)", WktWriter.Write(xyPoint));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory(DisplayName = "WKT reader rejects unsupported dimensions, arity and geometry forms")]
    [InlineData("POINT EMPTY")]
    [InlineData("POINT Z (1 2 3)")]
    [InlineData("POINT M (1 2 3)")]
    [InlineData("SRID=4326;POINT (1 2)")]
    [InlineData("POINT (NaN 0)")]
    [InlineData("POINT (Infinity 0)")]
    [InlineData("POINT (1)")]
    [InlineData("POINT (1 2 3)")]
    [InlineData("POINT (1 2) trailing")]
    [InlineData("POLYGON ((0 0, 1 0, 0 1))")]
    [InlineData("CIRCULARSTRING (0 0, 1 1, 2 0)")]
    [InlineData("CIRCLE (0 0, 1)")]
    [InlineData("ENVELOPE (0, 1, 1, 0)")]
    public void WktReader_RejectsUnsupportedInput(string text)
        => Assert.Throws<FormatException>(() => WktReader.ParseGeo(text));

    [Fact(DisplayName = "WKT parser applies collection nesting and coordinate position bounds")]
    public void WktReader_RejectsInputsOverConfiguredBounds()
    {
        const int nestedCollections = 33;
        string nested = string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION (", nestedCollections))
            + "POINT (0 0)"
            + new string(')', nestedCollections);
        Assert.Throws<FormatException>(() => WktReader.ParseGeo(nested));

        const int coordinateLimit = 1_000_000;
        string positions = string.Join(",", Enumerable.Repeat("0 0", coordinateLimit + 1));
        Assert.Throws<FormatException>(() => WktReader.ParseXY($"LINESTRING ({positions})"));

        Assert.Throws<FormatException>(() => WktReader.ParseXY("POINT (1\u00a02)"));
        Assert.Throws<FormatException>(() => WktReader.ParseXY("POINT (1e100 0)"));
        Assert.Throws<FormatException>(() => WktReader.ParseGeo("POINT ZM (1 2 3 4)"));
    }

    [Fact(DisplayName = "WKT writer rejects circles directly and inside collections")]
    public void WktWriter_CirclesAreUnsupported()
    {
        Assert.Throws<NotSupportedException>(() => WktWriter.Write(new GeoCircle(0, 0, 10)));
        Assert.Throws<NotSupportedException>(() => WktWriter.Write(new XYCircle(0, 0, 10)));
        Assert.Throws<NotSupportedException>(() => WktWriter.Write(
            new GeoGeometryCollection([new GeoPoint(0, 0), new GeoCircle(0, 0, 10)])));
        Assert.Throws<NotSupportedException>(() => WktWriter.Write(
            new XYGeometryCollection([new XYPoint(0, 0), new XYCircle(0, 0, 10)])));
    }

    [Fact(DisplayName = "Simplifiers preserve zero-tolerance identity and reduce line vertices")]
    public void Simplifiers_KeepZeroToleranceInstancesAndReduceLines()
    {
        var xyLine = new XYLineString([new XYPoint(0, 0), new XYPoint(1, 0.1f), new XYPoint(2, 0), new XYPoint(3, 0)]);
        Assert.Same(xyLine, XYSimplifier.Simplify(xyLine, 0));
        XYLineString xyReduced = XYSimplifier.Simplify(xyLine, 0.2f);
        Assert.Equal(2, xyReduced.Points.Count);
        Assert.Equal(xyLine.Points[0], xyReduced.Points[0]);
        Assert.Equal(xyLine.Points[^1], xyReduced.Points[^1]);

        var geoLine = new GeoLineString([
            new GeoPoint(0, 179),
            new GeoPoint(0.01, 180),
            new GeoPoint(0, -179),
        ]);
        Assert.Same(geoLine, GeoSimplifier.Simplify(geoLine, 0));
        GeoLineString geoReduced = GeoSimplifier.Simplify(geoLine, 2_000);
        Assert.Equal(geoLine.Points[0], geoReduced.Points[0]);
        Assert.Equal(geoLine.Points[^1], geoReduced.Points[^1]);
    }

    [Fact(DisplayName = "Simplifiers use known XY and Geo distances with deterministic tie-breaking")]
    public void Simplifiers_UseKnownPerpendicularAndCrossTrackDistances()
    {
        XYLineString xyLine = new([
            new XYPoint(0, 0),
            new XYPoint(1, 1),
            new XYPoint(2, 0),
        ]);
        Assert.Equal(3, XYSimplifier.Simplify(xyLine, 0.99f).Points.Count);
        Assert.Equal(2, XYSimplifier.Simplify(xyLine, 1f).Points.Count);

        XYLineString tiedLine = new([
            new XYPoint(0, 0),
            new XYPoint(1, 1),
            new XYPoint(2, 1),
            new XYPoint(3, 0),
        ]);
        XYLineString firstTie = XYSimplifier.Simplify(tiedLine, 0.95f);
        XYLineString repeatedTie = XYSimplifier.Simplify(tiedLine, 0.95f);
        Assert.Equal(firstTie, repeatedTie);
        Assert.Equal(new XYPoint(1, 1), firstTie.Points[1]);

        GeoLineString geoLine = new([
            new GeoPoint(0, 0),
            new GeoPoint(0.01, 1),
            new GeoPoint(0, 2),
        ]);
        Assert.Equal(3, GeoSimplifier.Simplify(geoLine, 1_100).Points.Count);
        Assert.Equal(2, GeoSimplifier.Simplify(geoLine, 1_200).Points.Count);

        GeoLineString datelineLine = new([
            new GeoPoint(0, 179),
            new GeoPoint(0.01, 180),
            new GeoPoint(0, -179),
        ]);
        GeoLineString simplifiedDateline = GeoSimplifier.Simplify(datelineLine, 1_200);
        Assert.Equal(datelineLine.Points[0], simplifiedDateline.Points[0]);
        Assert.Equal(datelineLine.Points[^1], simplifiedDateline.Points[^1]);
        Assert.Contains(simplifiedDateline.Points, static point => point.Longitude == 180);
        Assert.Contains(simplifiedDateline.Points, static point => point.Longitude == -180);
        Assert.All(simplifiedDateline.Points, static point => Assert.InRange(Math.Abs(point.Latitude), 0, 1e-6));
    }

    [Fact(DisplayName = "Simplifying polygon holes preserves closed valid output rings")]
    public void Simplifiers_RevalidateShellAndHoleRings()
    {
        GeoPolygon polygon = Assert.IsType<GeoPolygon>(WktReader.ParseGeo(
            "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0), (0.5 0.5, 0.5 1.5, 1.5 1.5, 1.5 0.5, 0.5 0.5))"));
        GeoPolygon simplified = GeoSimplifier.Simplify(polygon, 1);
        Assert.Single(simplified.Holes);
        Assert.Equal(simplified.Shell[0], simplified.Shell[^1]);
        Assert.Equal(simplified.Holes[0][0], simplified.Holes[0][^1]);
        _ = new GeoPolygon(simplified.Shell, simplified.Holes);

        XYPolygon xyPolygon = new(
        [
            new XYPoint(0, 0), new XYPoint(4, 0), new XYPoint(4, 4), new XYPoint(0, 4),
        ],
        [
            [
                new XYPoint(0.5f, 0.5f), new XYPoint(0.5f, 1.5f),
                new XYPoint(1.5f, 1.5f), new XYPoint(1.5f, 0.5f),
            ],
        ]);
        XYPolygon xySimplified = XYSimplifier.Simplify(xyPolygon, 0.01f);
        Assert.Single(xySimplified.Holes);
        Assert.Equal(xySimplified.Shell[0], xySimplified.Shell[^1]);
        Assert.Equal(xySimplified.Holes[0][0], xySimplified.Holes[0][^1]);
        _ = new XYPolygon(xySimplified.Shell, xySimplified.Holes);
    }

    [Fact(DisplayName = "Simplification rejects a shell chord that cuts through a retained hole")]
    public void XYSimplifier_RejectsSimplifiedShellHoleInteraction()
    {
        var polygon = new XYPolygon(
        [
            new XYPoint(0, 0), new XYPoint(4, 0), new XYPoint(5, -0.1f),
            new XYPoint(6, 0), new XYPoint(10, 0), new XYPoint(10, 10), new XYPoint(0, 10),
        ],
        [
            [
                new XYPoint(4.8f, -0.05f), new XYPoint(5.2f, -0.05f),
                new XYPoint(5.2f, 0.25f), new XYPoint(4.8f, 0.25f),
            ],
        ]);

        Assert.Throws<InvalidOperationException>(() => XYSimplifier.Simplify(polygon, 0.11f));
    }

    [Fact(DisplayName = "Simplifiers validate polygon topology and retain collection components")]
    public void Simplifiers_RevalidatePolygonsAndPreserveUnchangedComponents()
    {
        var xyPolygon = new XYPolygon([
            new XYPoint(0, 0), new XYPoint(2, 0), new XYPoint(2, 2), new XYPoint(0, 2),
        ]);
        Assert.Same(xyPolygon, XYSimplifier.Simplify(xyPolygon, 0));
        Assert.Throws<InvalidOperationException>(() => XYSimplifier.Simplify(xyPolygon, 100));

        var geoPolygon = new GeoPolygon([
            new GeoPoint(0, 0), new GeoPoint(0, 1), new GeoPoint(1, 1), new GeoPoint(1, 0),
        ]);
        Assert.Same(geoPolygon, GeoSimplifier.Simplify(geoPolygon, 0));
        Assert.Throws<InvalidOperationException>(() => GeoSimplifier.Simplify(geoPolygon, 500_000));

        var point = new XYPoint(5, 6);
        var rectangle = new XYRectangle(10, 10, 20, 20);
        var collection = new XYGeometryCollection([
            point,
            rectangle,
            new XYLineString([new XYPoint(0, 0), new XYPoint(1, 0.1f), new XYPoint(2, 0)]),
        ]);
        Assert.Same(collection, XYSimplifier.Simplify(collection, 0));
        XYGeometryCollection reduced = XYSimplifier.Simplify(collection, 0.2f);
        Assert.Equal(point, reduced.Geometries[0]);
        Assert.Equal(rectangle, reduced.Geometries[1]);
        Assert.IsType<XYLineString>(reduced.Geometries[2]);
    }

    [Theory(DisplayName = "Simplifiers reject non-finite and negative tolerances")]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void GeoSimplifier_RejectsInvalidTolerance(double tolerance)
        => Assert.Throws<ArgumentOutOfRangeException>(() => GeoSimplifier.Simplify(
            new GeoLineString([new GeoPoint(0, 0), new GeoPoint(1, 1)]), tolerance));

    [Theory(DisplayName = "XY simplifier rejects non-finite and negative tolerances")]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void XYSimplifier_RejectsInvalidTolerance(float tolerance)
        => Assert.Throws<ArgumentOutOfRangeException>(() => XYSimplifier.Simplify(
            new XYLineString([new XYPoint(0, 0), new XYPoint(1, 1)]), tolerance));
}

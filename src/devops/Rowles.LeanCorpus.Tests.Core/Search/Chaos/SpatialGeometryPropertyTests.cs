using FsCheck;
using FsCheck.Xunit;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Chaos)]
[Area(TestArea.Search)]
public sealed class SpatialGeometryPropertyTests
{
    [Property(DisplayName = "Geo sortable encoding is monotonic", MaxTest = 200, StartSize = 1, EndSize = 128)]
    public void GeoEncoding_IsMonotonic(NonEmptyArray<byte> input)
    {
        byte[] values = input.Get;
        int previous = int.MinValue;
        foreach (byte value in values.Order())
        {
            double latitude = -90.0 + value / 255.0 * 180.0;
            int encoded = GeoEncodingUtils.EncodeLatFloor(latitude);
            Assert.True(encoded >= previous);
            previous = encoded;
        }
    }

    [Property(DisplayName = "XY sortable encoding round-trips generated finite values", MaxTest = 200, StartSize = 1, EndSize = 128)]
    public void XYEncoding_RoundTripsFiniteValues(NonEmptyArray<byte> input)
    {
        Span<byte> encoded = stackalloc byte[4];
        foreach (byte value in input.Get)
        {
            float coordinate = value - 127.0f;
            XYEncodingUtils.Encode(coordinate, encoded);
            Assert.Equal(coordinate, XYEncodingUtils.Decode(encoded));
        }
    }

    [Property(DisplayName = "Equivalent ring closure and winding canonicalise identically", MaxTest = 200, StartSize = 1, EndSize = 128)]
    public void GeoPolygon_CanonicalisationIsDeterministic(NonEmptyArray<byte> input)
    {
        byte[] values = input.Get;
        double width = 1 + values[0] % 20;
        double height = 1 + values[^1] % 20;
        var counterClockwise = new[]
        {
            new GeoPoint(0, 0),
            new GeoPoint(0, width),
            new GeoPoint(height, width),
            new GeoPoint(height, 0),
        };
        var clockwiseWithClosure = counterClockwise.Reverse().Append(counterClockwise[^1]);

        var first = new GeoPolygon(counterClockwise);
        var second = new GeoPolygon(clockwiseWithClosure);
        Assert.Equal(first, second);

        Assert.Equal(first, new GeoPolygon(first.Shell));
    }

    [Property(DisplayName = "Geo floor and ceiling bounds contain generated coordinates", MaxTest = 200, StartSize = 1, EndSize = 128)]
    public void GeoEncoding_FloorAndCeilingContainValues(NonEmptyArray<byte> input)
    {
        foreach (byte value in input.Get)
        {
            double latitude = -90.0 + value / 255.0 * 180.0;
            double longitude = -180.0 + value / 255.0 * 360.0;
            const double quantisationTolerance = 1e-6;
            Assert.True(GeoEncodingUtils.DecodeLat(GeoEncodingUtils.EncodeLatFloor(latitude)) <= latitude + quantisationTolerance);
            Assert.True(GeoEncodingUtils.DecodeLat(GeoEncodingUtils.EncodeLatCeil(latitude)) >= latitude - quantisationTolerance);
            Assert.True(GeoEncodingUtils.DecodeLon(GeoEncodingUtils.EncodeLonFloor(longitude)) <= longitude + quantisationTolerance);
            Assert.True(GeoEncodingUtils.DecodeLon(GeoEncodingUtils.EncodeLonCeil(longitude)) >= longitude - quantisationTolerance);
        }
    }

    [Property(DisplayName = "Generated invalid polygon topology fails boundedly", MaxTest = 200, StartSize = 1, EndSize = 128)]
    public void GeoPolygon_InvalidBowTieIsRejected(NonEmptyArray<byte> input)
    {
        double width = 1 + input.Get[0] % 100;
        double height = 1 + input.Get[^1] % 40;
        Assert.Throws<ArgumentException>(() => new GeoPolygon([
            new GeoPoint(0, 0),
            new GeoPoint(height, width),
            new GeoPoint(0, width),
            new GeoPoint(height, 0)]));
    }

    [Property(DisplayName = "Generated dateline polygons canonicalise deterministically", MaxTest = 200, StartSize = 1, EndSize = 128)]
    public void GeoPolygon_DatelineCanonicalisationIsDeterministic(NonEmptyArray<byte> input)
    {
        double south = -50 + input.Get[0] % 20;
        double north = south + 20 + input.Get[^1] % 20;
        var points = new[]
        {
            new GeoPoint(south + 10, 179),
            new GeoPoint(north, -179),
            new GeoPoint(south, -179),
        };

        var first = new GeoPolygon(points);
        var second = new GeoPolygon(first.Shell);
        Assert.Equal(first, second);
        Assert.Contains(first.Shell, point => point.Longitude == 180);
        Assert.Contains(first.Shell, point => point.Longitude == -180);
    }
}

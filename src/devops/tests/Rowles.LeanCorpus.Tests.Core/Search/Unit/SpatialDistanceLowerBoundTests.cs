using System.Buffers.Binary;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Searcher.Internal;
using Rowles.LeanCorpus.Search.XY;
using Xunit;

namespace Rowles.LeanCorpus.Tests.Core.Search.Sorting;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class SpatialDistanceLowerBoundTests
{
    [Fact]
    public void GeoCellLowerBoundNeverExceedsSampledHaversineDistance()
    {
        var random = new Random(0x7015);
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            double firstLatitude = random.NextDouble() * 180 - 90;
            double secondLatitude = random.NextDouble() * 180 - 90;
            double minimumLatitude = Math.Min(firstLatitude, secondLatitude);
            double maximumLatitude = Math.Max(firstLatitude, secondLatitude);
            double firstLongitude = random.NextDouble() * 360 - 180;
            double secondLongitude = random.NextDouble() * 360 - 180;
            double minimumLongitude = Math.Min(firstLongitude, secondLongitude);
            double maximumLongitude = Math.Max(firstLongitude, secondLongitude);
            double originLatitude = random.NextDouble() * 180 - 90;
            double originLongitude = random.NextDouble() * 360 - 180;
            uint minLonCode = EncodeGeo(GeoEncodingUtils.EncodeLon(minimumLongitude));
            uint maxLonCode = EncodeGeo(GeoEncodingUtils.EncodeLon(maximumLongitude));
            uint minLatCode = EncodeGeo(GeoEncodingUtils.EncodeLat(minimumLatitude));
            uint maxLatCode = EncodeGeo(GeoEncodingUtils.EncodeLat(maximumLatitude));

            double lowerBound = SpatialDistanceLowerBound.GeoMetres(
                originLatitude, originLongitude, minLonCode, maxLonCode, minLatCode, maxLatCode);
            for (int sample = 0; sample < 8; sample++)
            {
                double latitude = minimumLatitude + random.NextDouble() * (maximumLatitude - minimumLatitude);
                double longitude = minimumLongitude + random.NextDouble() * (maximumLongitude - minimumLongitude);
                double actual = GeoEncodingUtils.HaversineDistance(originLatitude, originLongitude, latitude, longitude);
                Assert.True(lowerBound <= actual,
                    $"Lower bound {lowerBound:R} exceeded sampled distance {actual:R}; origin=({originLatitude:R},{originLongitude:R}), cell=({minimumLatitude:R}..{maximumLatitude:R},{minimumLongitude:R}..{maximumLongitude:R}), sample=({latitude:R},{longitude:R}).");
            }
        }

        AssertGeoCellBound(new GeoPoint(89.999, 179.999), -180, -179.99, 89.9, 90);
        AssertGeoCellBound(new GeoPoint(-89.999, -179.999), 179.99, 180, -90, -89.9);
    }

    [Fact]
    public void XYCellLowerBoundNeverExceedsSampledSquaredDistance()
    {
        var random = new Random(0x7016);
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            float minimumX = (float)(random.NextDouble() * 2_000_000 - 1_000_000);
            float maximumX = minimumX + (float)(random.NextDouble() * 20_000);
            float minimumY = (float)(random.NextDouble() * 2_000_000 - 1_000_000);
            float maximumY = minimumY + (float)(random.NextDouble() * 20_000);
            float originX = (float)(random.NextDouble() * 2_000_000 - 1_000_000);
            float originY = (float)(random.NextDouble() * 2_000_000 - 1_000_000);
            uint minX = EncodeFloat(minimumX);
            uint maxX = EncodeFloat(maximumX);
            uint minY = EncodeFloat(minimumY);
            uint maxY = EncodeFloat(maximumY);

            double lowerBound = SpatialDistanceLowerBound.XYSquared(originX, originY, minX, maxX, minY, maxY);
            for (int sample = 0; sample < 8; sample++)
            {
                float x = minimumX + (float)random.NextDouble() * (maximumX - minimumX);
                float y = minimumY + (float)random.NextDouble() * (maximumY - minimumY);
                double dx = (double)x - originX;
                double dy = (double)y - originY;
                Assert.True(lowerBound <= dx * dx + dy * dy);
            }
        }
    }

    private static void AssertGeoCellBound(
        GeoPoint origin,
        double minimumLongitude,
        double maximumLongitude,
        double minimumLatitude,
        double maximumLatitude)
    {
        double lowerBound = SpatialDistanceLowerBound.GeoMetres(
            origin.Latitude,
            origin.Longitude,
            EncodeGeo(GeoEncodingUtils.EncodeLon(minimumLongitude)),
            EncodeGeo(GeoEncodingUtils.EncodeLon(maximumLongitude)),
            EncodeGeo(GeoEncodingUtils.EncodeLat(minimumLatitude)),
            EncodeGeo(GeoEncodingUtils.EncodeLat(maximumLatitude)));
        for (int i = 0; i <= 10; i++)
        {
            double latitude = minimumLatitude + (maximumLatitude - minimumLatitude) * i / 10;
            for (int j = 0; j <= 10; j++)
            {
                double longitude = minimumLongitude + (maximumLongitude - minimumLongitude) * j / 10;
                double actual = GeoEncodingUtils.HaversineDistance(
                    origin.Latitude, origin.Longitude, latitude, longitude);
                Assert.True(lowerBound <= actual);
            }
        }
    }

    private static uint EncodeGeo(int value)
        => unchecked((uint)(value ^ int.MinValue));

    private static uint EncodeFloat(float value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        XYEncodingUtils.Encode(value, bytes);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }
}

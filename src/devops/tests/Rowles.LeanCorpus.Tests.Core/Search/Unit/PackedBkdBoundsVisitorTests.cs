using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Search.Searcher.Internal;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Tests.Core.Search.Unit;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class PackedBkdBoundsVisitorTests
{
    [Fact(DisplayName = "Packed bounds visitor includes edges and deduplicates a dateline union")]
    public void GeoDatelineUnion_IncludesEdgesExcludesGreenwichAndDeduplicatesDocuments()
    {
        string path = Path.Combine(Path.GetTempPath(), "leancorpus-packed-bounds-" + Guid.NewGuid().ToString("N") + ".pbkd");
        using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 2));
        AppendGeo(buffer, 0, -175, 0);
        AppendGeo(buffer, 0, 175, 0);
        AppendGeo(buffer, 1, 179, 1);
        AppendGeo(buffer, 2, 0, 0);
        PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

        try
        {
            using var reader = PackedBkdReader.Open(path);
            var matched = new RoaringBitmap();
            var documents = new List<int>();
            var westRange = CreateGeoVisitor(170, 180, matched, documents);
            Assert.True(reader.Intersect("location", ref westRange));
            var eastRange = CreateGeoVisitor(-180, -170, matched, documents);
            Assert.True(reader.Intersect("location", ref eastRange));

            Assert.Equal([0, 1], documents.Order());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact(DisplayName = "Packed bounds visitor matches inclusive XY rectangle edges")]
    public void XYRectangle_IncludesBothInclusiveEdges()
    {
        string path = Path.Combine(Path.GetTempPath(), "leancorpus-packed-bounds-" + Guid.NewGuid().ToString("N") + ".pbkd");
        using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 2));
        AppendXy(buffer, 0, 1, 2);
        AppendXy(buffer, 1, 2, 3);
        AppendXy(buffer, 2, 1, 3);
        AppendXy(buffer, 3, 2.01f, 3);
        PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["position"] = buffer });

        try
        {
            using var reader = PackedBkdReader.Open(path);
            byte[] minimum = new byte[8];
            byte[] maximum = new byte[8];
            XYEncodingUtils.Encode(1, minimum.AsSpan(0, 4));
            XYEncodingUtils.Encode(2, minimum.AsSpan(4, 4));
            XYEncodingUtils.Encode(2, maximum.AsSpan(0, 4));
            XYEncodingUtils.Encode(3, maximum.AsSpan(4, 4));
            var documents = new List<int>();
            var visitor = new PackedBkdBoundsVisitor(minimum, maximum, new RoaringBitmap(), documents);

            Assert.True(reader.Intersect("position", ref visitor));
            Assert.Equal([0, 1, 2], documents.Order());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static PackedBkdBoundsVisitor CreateGeoVisitor(
        double minLongitude,
        double maxLongitude,
        RoaringBitmap matched,
        List<int> documents)
    {
        byte[] minimum = new byte[8];
        byte[] maximum = new byte[8];
        GeoEncodingUtils.WriteLonSortable(minLongitude, minimum);
        GeoEncodingUtils.WriteLatSortable(-90, minimum.AsSpan(4));
        GeoEncodingUtils.WriteLonSortable(maxLongitude, maximum);
        GeoEncodingUtils.WriteLatSortable(90, maximum.AsSpan(4));
        return new PackedBkdBoundsVisitor(minimum, maximum, matched, documents);
    }

    private static void AppendGeo(PackedBkdFieldBuffer buffer, int docId, double longitude, double latitude)
    {
        byte[] point = new byte[8];
        GeoEncodingUtils.WriteLonSortable(longitude, point);
        GeoEncodingUtils.WriteLatSortable(latitude, point.AsSpan(4));
        buffer.Append(point, docId);
    }

    private static void AppendXy(PackedBkdFieldBuffer buffer, int docId, float x, float y)
    {
        byte[] point = new byte[8];
        XYEncodingUtils.Encode(x, point);
        XYEncodingUtils.Encode(y, point.AsSpan(4));
        buffer.Append(point, docId);
    }
}

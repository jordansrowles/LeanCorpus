using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;
using Rowles.LeanCorpus.Search.XY;
using Xunit;

namespace Rowles.LeanCorpus.Tests.AOTSmoke;

public sealed class PackedBkdSmokeTests
{
    [Fact]
    public void PackedBkdRoundTripsUnderNativeAot()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"lc-aot-packed-bkd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "points.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 2));
            Append(buffer, 0, 0, 0);
            Append(buffer, 1, 1, 1);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            using var reader = PackedBkdReader.Open(path);
            var visitor = new VisitAllVisitor();
            Assert.True(reader.Intersect("location", ref visitor));
            Assert.Equal([0, 1], visitor.Documents.Order());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static void Append(PackedBkdFieldBuffer buffer, float x, float y, int document)
    {
        byte[] packed = new byte[8];
        XYEncodingUtils.Encode(x, packed.AsSpan(0, 4));
        XYEncodingUtils.Encode(y, packed.AsSpan(4, 4));
        buffer.Append(packed, document);
    }

    private struct VisitAllVisitor : IPackedBkdIntersectVisitor
    {
        internal List<int> Documents { get; }

        public VisitAllVisitor()
        {
            Documents = [];
        }

        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => PackedBkdCellRelation.Inside;

        public void Visit(int docId) => Documents.Add(docId);

        public void Visit(int docId, ReadOnlySpan<byte> packedValue)
            => Documents.Add(docId);
    }
}

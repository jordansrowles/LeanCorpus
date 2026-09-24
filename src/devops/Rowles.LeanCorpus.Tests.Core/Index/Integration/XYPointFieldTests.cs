using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class XYPointFieldTests
{
    [Fact(DisplayName = "XY point field writes packed points and multi-valued binary DocValues")]
    public void XYPointField_WritesPackedPointsAndBinaryDocValues()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), "leancorpus-xy-point-field", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directoryPath);
        try
        {
            using var directory = new MMapDirectory(directoryPath);
            using (var writer = new IndexWriter(directory, new IndexWriterConfig
            {
                BKDMaxLeafSize = 2,
                MergePolicy = NoMergePolicy.Instance
            }))
            {
                var first = new LeanDocument();
                first.Add(new XYPointField("position", 1.25f, -4.5f));
                first.Add(new XYPointField("position", 3.75f, 8.5f));
                writer.AddDocument(first);

                var second = new LeanDocument();
                second.Add(new XYPointField("position", -9.0f, 2.0f));
                writer.AddDocument(second);
                writer.Commit();
            }

            string packedPath = Assert.Single(System.IO.Directory.GetFiles(directoryPath, "*.pbkd"));
            Assert.Empty(System.IO.Directory.GetFiles(directoryPath, "*.bkd"));
            using var packedReader = PackedBkdReader.Open(packedPath);
            PackedBkdFieldMetadata metadata = packedReader.GetFieldMetadata("position");
            Assert.Equal(PackedBkdConfig.Point2D(maxPointsPerLeaf: 2), metadata.Config);
            Assert.Equal(3, metadata.PointCount);
            Assert.Equal(2, metadata.DocumentCount);

            using var searcher = new IndexSearcher(directory);
            SegmentReader segment = Assert.Single(searcher.GetSegmentReaders());
            Assert.True(segment.TryGetBinaryDocValues("position", 0, out var values));
            Assert.Equal(2, values.Count);
            Assert.Equal(8, values[0].Length);
            Assert.Equal(1.25f, XYEncodingUtils.Decode(values[0].AsSpan(0, 4)));
            Assert.Equal(-4.5f, XYEncodingUtils.Decode(values[0].AsSpan(4, 4)));
            Assert.Equal(3.75f, XYEncodingUtils.Decode(values[1].AsSpan(0, 4)));
            Assert.Equal(8.5f, XYEncodingUtils.Decode(values[1].AsSpan(4, 4)));

            var field = new XYPointField("position", 1, 2);
            Assert.Equal(FieldType.Binary, field.FieldType);
            Assert.False(field.IsStored);
            Assert.True(field.IsIndexed);
            Assert.True(field.StoreDocValues);
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(directoryPath);
        }
    }

    [Theory]
    [InlineData(float.NaN, 1.0f)]
    [InlineData(float.PositiveInfinity, 1.0f)]
    [InlineData(1.0f, float.NegativeInfinity)]
    public void XYPointField_RejectsNonFiniteCoordinates(float x, float y)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new XYPointField("position", x, y));

    [Fact]
    public void XYPointField_AllowsFiniteExtremeCoordinates()
    {
        var field = new XYPointField("position", float.MaxValue, float.MinValue);

        Assert.Equal(float.MaxValue, field.X);
        Assert.Equal(float.MinValue, field.Y);
    }
}

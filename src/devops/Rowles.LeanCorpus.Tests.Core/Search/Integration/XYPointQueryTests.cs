using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Xunit;

namespace Rowles.LeanCorpus.Tests.Core.Search.XY;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class XYPointQueryTests : IDisposable
{
    private readonly string _directoryPath = Path.Combine(
        Path.GetTempPath(), "leancorpus_xy_query_" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
        => TestDirectoryFixture.TryDeleteDirectory(_directoryPath);

    [Fact]
    public void BoundingBox_IncludesEdgesAndMatchesAnyPointOnce()
    {
        using (var directory = new MMapDirectory(_directoryPath))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { BKDMaxLeafSize = 2 }))
        {
            AddPoint(writer, -2, 2);
            AddPoint(writer, 2, -2);

            var multiValued = new LeanDocument();
            multiValued.Add(new XYPointField("position", 20, 20));
            multiValued.Add(new XYPointField("position", 0, 0));
            writer.AddDocument(multiValued);

            AddPoint(writer, 2.0001f, 0);
            writer.Commit();
        }

        using var directoryReader = new MMapDirectory(_directoryPath);
        using var searcher = new IndexSearcher(directoryReader);
        var results = searcher.Search(
            new XYBoundingBoxQuery("position", new XYRectangle(-2, -2, 2, 2)),
            10,
            TestContext.Current.CancellationToken);

        Assert.Equal(3, results.TotalHits);
    }

    [Fact]
    public void Distance_UsesZeroRadiusAndInclusiveEuclideanBoundary()
    {
        using (var directory = new MMapDirectory(_directoryPath))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { BKDMaxLeafSize = 2 }))
        {
            AddPoint(writer, 0, 0);
            AddPoint(writer, 3, 4);
            AddPoint(writer, 3.0001f, 4);

            var multiValued = new LeanDocument();
            multiValued.Add(new XYPointField("position", 10, 10));
            multiValued.Add(new XYPointField("position", 0, 1));
            writer.AddDocument(multiValued);
            writer.Commit();
        }

        using var directoryReader = new MMapDirectory(_directoryPath);
        using var searcher = new IndexSearcher(directoryReader);
        var exact = searcher.Search(
            new XYDistanceQuery("position", new XYPoint(0, 0), 0),
            10,
            TestContext.Current.CancellationToken);
        var radiusFive = searcher.Search(
            new XYDistanceQuery("position", new XYPoint(0, 0), 5),
            10,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, exact.TotalHits);
        Assert.Equal(3, radiusFive.TotalHits);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Distance_RejectsNegativeOrNonFiniteRadius(float radius)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new XYDistanceQuery("position", new XYPoint(0, 0), radius));

    [Fact]
    public void Distance_HandlesExtremeFiniteCoordinatesWithoutOverflow()
    {
        using (var directory = new MMapDirectory(_directoryPath))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            AddPoint(writer, float.MaxValue, float.MaxValue);
            AddPoint(writer, 0, float.MaxValue);
            writer.Commit();
        }

        using var directoryReader = new MMapDirectory(_directoryPath);
        using var searcher = new IndexSearcher(directoryReader);
        var result = searcher.Search(
            new XYDistanceQuery("position", new XYPoint(float.MaxValue, float.MaxValue), float.MaxValue),
            10,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.TotalHits);
    }

    private static void AddPoint(IndexWriter writer, float x, float y)
    {
        var document = new LeanDocument();
        document.Add(new XYPointField("position", x, y));
        writer.AddDocument(document);
    }
}

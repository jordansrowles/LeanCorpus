using System.Globalization;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Aggregations;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Numeric field accessors resolved once per segment: results match a brute-force oracle with
/// deletes and many segments, no state or value outlives its lease under eviction, retained-usage
/// accounting stays within its limits, and incompatible segment layouts still fail.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class NumericSegmentAccessorTests : IClassFixture<TestDirectoryFixture>
{
    private const int DocumentCount = 1_200;

    private readonly TestDirectoryFixture _fixture;

    public NumericSegmentAccessorTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Numeric Accessors: Stats Match An Oracle Across Segments And Deletes")]
    public void NumericAccessors_StatsMatchOracle()
    {
        int[] deleted = Enumerable.Range(0, DocumentCount).Where(static i => i % 13 == 5).ToArray();
        using var directory = Build("accessor_oracle", segments: 9, deleted);
        using var searcher = new IndexSearcher(directory);

        var (results, aggregations) = searcher.SearchWithAggregations(
            new TermQuery("kind", "item"), 10,
            new AggregationRequest("price", "price"), new AggregationRequest("count", "count"));

        var live = Enumerable.Range(0, DocumentCount).Where(i => !deleted.Contains(i) && HasPrice(i)).ToArray();
        Assert.Equal(DocumentCount - deleted.Length, results.TotalHits);
        var price = aggregations.Single(static a => a.Name == "price");
        Assert.Equal(live.Length, price.Count);
        Assert.Equal(live.Min(Price), price.Min);
        Assert.Equal(live.Max(Price), price.Max);
        Assert.Equal(live.Sum(Price), price.Sum, 6);
        var count = aggregations.Single(static a => a.Name == "count");
        Assert.Equal(DocumentCount - deleted.Length, count.Count);
        Assert.Equal(Enumerable.Range(0, DocumentCount).Where(i => !deleted.Contains(i)).Sum(static i => (double)i * 3), count.Sum, 6);
    }

    [Fact(DisplayName = "Numeric Accessors: Eviction Keeps Results And Cache Limits")]
    public void NumericAccessors_EvictionKeepsResultsAndCacheLimits()
    {
        using var directory = Build("accessor_eviction", segments: 10, deleted: []);
        using var reference = new IndexSearcher(directory, new IndexSearcherConfig { MaxCachedSegmentReaders = 16 });
        using var evicting = new IndexSearcher(directory, new IndexSearcherConfig
        {
            MaxCachedSegmentReaders = 1,
            MaxCachedSegmentReaderBytes = 64 * 1024
        });

        var expected = Stats(reference);
        var actual = Stats(evicting);
        Assert.Equal(expected, actual);

        // A cached accessor holds only a representation descriptor, so every state can still be
        // released and evicted, and retained usage returns within the configured limits.
        var metrics = evicting.SegmentReaderCacheMetrics;
        Assert.True(metrics.EntryCount <= 1, $"entries {metrics.EntryCount}");
        Assert.True(metrics.EvictionCount > 0);
        Assert.True(metrics.RetainedBytes <= metrics.MaximumRetainedBytes,
            $"retained {metrics.RetainedBytes} of {metrics.MaximumRetainedBytes}");

        // Aggregation results are plain values: they stay valid after the searcher is disposed.
        evicting.Dispose();
        Assert.Equal(expected, actual);
        Assert.Throws<ObjectDisposedException>(() => Stats(evicting));
    }

    [Fact(DisplayName = "Numeric Accessors: Incompatible Layouts Across Segments Still Throw")]
    public void NumericAccessors_IncompatibleLayoutsAcrossSegments_Throw()
    {
        string path = NewPath("accessor_mixed");
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            writer.AddDocument(Document("0", static d => d.Add(new NumericField("value", 1.5))));
            writer.Commit();
            writer.AddDocument(Document("1", static d => d.Add(new Int64Field("value", 2))));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            searcher.SearchWithAggregations(new TermQuery("kind", "item"), 10, new AggregationRequest("value", "value")));
        Assert.Equal("Numeric field 'value' has incompatible DocValues representations across segments.", exception.Message);
    }

    [Fact(DisplayName = "Numeric Accessors: A Non-Numeric Segment Representation Still Throws")]
    public void NumericAccessors_NonNumericSegmentRepresentation_Throws()
    {
        string path = NewPath("accessor_non_numeric");
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            writer.AddDocument(Document("0", static d => d.Add(new NumericField("value", 1.5))));
            writer.Commit();
            writer.AddDocument(Document("1", static d => d.Add(new StringField("value", "text", stored: false))));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            searcher.SearchWithAggregations(new TermQuery("kind", "item"), 10, new AggregationRequest("value", "value")));
        Assert.Contains("has an incompatible non-numeric representation in segment", exception.Message, StringComparison.Ordinal);
    }

    private static (long Count, double Min, double Max, double Sum) Stats(IndexSearcher searcher)
    {
        var (_, aggregations) = searcher.SearchWithAggregations(new TermQuery("kind", "item"), 10, new AggregationRequest("price", "price"));
        var price = Assert.Single(aggregations);
        return (price.Count, price.Min, price.Max, price.Sum);
    }

    // One document in seven has no price; the rest have a deterministic value with repeats.
    private static bool HasPrice(int i) => i % 7 != 3;

    private static double Price(int i) => ((i * 37) % 500) * 0.25;

    private MMapDirectory Build(string name, int segments, int[] deleted)
    {
        var directory = new MMapDirectory(NewPath(name));
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = (int)Math.Ceiling(DocumentCount / (double)segments),
            MergePolicy = NoMergePolicy.Instance
        });
        for (int i = 0; i < DocumentCount; i++)
        {
            int id = i;
            writer.AddDocument(Document(id.ToString(CultureInfo.InvariantCulture), d =>
            {
                if (HasPrice(id))
                    d.Add(new NumericField("price", Price(id)));
                d.Add(new Int64Field("count", (long)id * 3));
            }));
        }
        writer.Commit();
        foreach (int id in deleted)
            writer.DeleteDocuments(new TermQuery("id", id.ToString(CultureInfo.InvariantCulture)));
        writer.Commit();
        return directory;
    }

    private static LeanDocument Document(string id, Action<LeanDocument> fields)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id, stored: true));
        document.Add(new StringField("kind", "item", stored: false));
        fields(document);
        return document;
    }

    private string NewPath(string name)
    {
        string path = Path.Combine(_fixture.Path, name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

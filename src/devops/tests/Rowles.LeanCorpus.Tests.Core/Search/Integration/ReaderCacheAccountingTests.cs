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
/// Retained-usage accounting when nested leases skip their own refresh: usage gained while a
/// state is leased is measured when its last lease is released, limits are then enforced by
/// eviction, and concurrent acquire and release lose no refresh.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class ReaderCacheAccountingTests : IClassFixture<TestDirectoryFixture>
{
    private const int DocumentCount = 2_000;

    private readonly TestDirectoryFixture _fixture;

    public ReaderCacheAccountingTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Reader Cache: Usage Gained Under Nested Leases Is Counted At The Last Release")]
    public void ReaderCache_UsageGainedUnderNestedLeases_IsCountedAtLastRelease()
    {
        using var directory = Build("accounting_nested", segments: 1);
        using var searcher = new IndexSearcher(directory);
        var reader = Assert.Single(searcher.GetSegmentReaders());

        // Hold an outer pin while queries load DocValues and stored fields: their own leases are
        // nested, so they do not refresh the estimate themselves.
        reader.WithQueryPin(searcher, static (_, s) =>
        {
            RunWorkload(s);
            return 0;
        });

        var afterPin = searcher.SegmentReaderCacheMetrics;
        Assert.True(afterPin.DocValuesBytes > 0, "DocValues loaded under the pin must be counted after its release");
        Assert.True(afterPin.StoredFieldsBytes > 0, "stored fields loaded under the pin must be counted after its release");

        // A searcher that does the same work without an outer pin measures the same usage.
        using var reference = new IndexSearcher(directory);
        RunWorkload(reference);
        Assert.Equal(reference.SegmentReaderCacheMetrics.RetainedBytes, afterPin.RetainedBytes);
    }

    [Fact(DisplayName = "Reader Cache: Byte Limit Evicts Once Nested Leases Are Released")]
    public void ReaderCache_ByteLimit_EvictsOnceNestedLeasesAreReleased()
    {
        using var directory = Build("accounting_limit", segments: 6);
        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig { MaxCachedSegmentReaderBytes = 16 * 1024 });
        var first = searcher.GetSegmentReaders()[0];

        first.WithQueryPin(searcher, static (_, s) =>
        {
            RunWorkload(s);
            return 0;
        });

        var metrics = searcher.SegmentReaderCacheMetrics;
        Assert.True(metrics.EvictionCount > 0, "states over the byte limit must be evicted after their last release");
        Assert.True(metrics.RetainedBytes <= metrics.MaximumRetainedBytes,
            $"retained {metrics.RetainedBytes} of {metrics.MaximumRetainedBytes}");
    }

    [Fact(DisplayName = "Reader Cache: Concurrent Leases Lose No Refresh")]
    public async Task ReaderCache_ConcurrentLeases_LoseNoRefresh()
    {
        using var directory = Build("accounting_concurrent", segments: 4);
        using var concurrent = new IndexSearcher(directory);
        var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
        {
            for (int round = 0; round < 5; round++)
                RunWorkload(concurrent);
        }, TestContext.Current.CancellationToken)).ToArray();
        await Task.WhenAll(tasks);

        using var sequential = new IndexSearcher(directory);
        RunWorkload(sequential);

        var concurrentMetrics = concurrent.SegmentReaderCacheMetrics;
        var sequentialMetrics = sequential.SegmentReaderCacheMetrics;
        Assert.Equal(sequentialMetrics.EntryCount, concurrentMetrics.EntryCount);
        Assert.Equal(sequentialMetrics.DocValuesBytes, concurrentMetrics.DocValuesBytes);
        Assert.Equal(sequentialMetrics.StoredFieldsBytes, concurrentMetrics.StoredFieldsBytes);
        Assert.Equal(sequentialMetrics.RetainedBytes, concurrentMetrics.RetainedBytes);
    }

    // Facets (sorted-set DocValues), a numeric aggregation (sorted-numeric DocValues) and stored
    // fields for the top hits.
    private static void RunWorkload(IndexSearcher searcher)
    {
        var query = new TermQuery("kind", "doc");
        var (_, facets) = searcher.SearchWithFacets(query, 10, "topic");
        Assert.NotEmpty(Assert.Single(facets).Buckets);
        var (results, aggregations) = searcher.SearchWithAggregations(query, 10, new AggregationRequest("price", "price"));
        Assert.Equal(DocumentCount, Assert.Single(aggregations).Count);
        foreach (var hit in results.ScoreDocs)
            Assert.NotEmpty(searcher.GetStoredFields(hit.DocId)["id"]);
    }

    private MMapDirectory Build(string name, int segments)
    {
        string path = Path.Combine(_fixture.Path, name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = (int)Math.Ceiling(DocumentCount / (double)segments),
            MergePolicy = NoMergePolicy.Instance
        });
        for (int i = 0; i < DocumentCount; i++)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", i.ToString(CultureInfo.InvariantCulture), stored: true));
            document.Add(new StringField("kind", "doc"));
            document.Add(new StringField("topic", "topic" + (i % 17).ToString(CultureInfo.InvariantCulture)));
            document.Add(new StringField("topic", "topic" + ((i + 5) % 17).ToString(CultureInfo.InvariantCulture)));
            document.Add(new NumericField("price", i * 0.75));
            writer.AddDocument(document);
        }
        writer.Commit();
        return directory;
    }
}

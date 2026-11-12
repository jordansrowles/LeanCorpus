using System.Globalization;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Covers combined-fields statistics prepared under one query pin per segment: segment states stay
/// valid during concurrent refresh, deleted documents stay out of the union document frequency,
/// pinned and unpinned facade reads agree, and the preparation keeps to an allocation budget.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class CombinedFieldsQueryPinTests : IClassFixture<TestDirectoryFixture>
{
    private const int DocumentCount = 1_200;
    private static readonly string[] Fields = ["title", "body"];

    private readonly TestDirectoryFixture _fixture;

    public CombinedFieldsQueryPinTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Query Pin: Pinned Reads Match Unpinned Reads")]
    public void QueryPin_PinnedReadsMatchUnpinnedReads()
    {
        using var directory = Build("pin_reads", segments: 3, deleteEvery: 5);
        using var searcher = new IndexSearcher(directory);
        foreach (var reader in searcher.GetSegmentReaders())
        {
            bool[] unpinned = Enumerable.Range(0, reader.MaxDoc).Select(reader.IsLive).ToArray();
            bool[] pinned = reader.WithQueryPin(reader.MaxDoc, static (pinnedReader, maxDoc) =>
                Enumerable.Range(0, maxDoc).Select(pinnedReader.IsLive).ToArray());
            Assert.Equal(unpinned, pinned);
            Assert.Contains(false, pinned);
        }
    }

    [Fact(DisplayName = "Query Pin: Releases The Pin When The Operation Throws")]
    public void QueryPin_ReleasesWhenOperationThrows()
    {
        using var directory = Build("pin_throw", segments: 2, deleteEvery: 0);
        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig { MaxCachedSegmentReaders = 1 });
        var reader = searcher.GetSegmentReaders()[0];

        Assert.Throws<InvalidOperationException>(() => reader.WithQueryPin(0, static (_, _) => ThrowExpected()));

        // A leaked pin would keep the first state leased, so the cache could not evict it while the
        // search loads the second segment.
        Assert.Equal(DocumentCount / 2, Search(searcher).TotalHits);
        Assert.True(searcher.SegmentReaderCacheMetrics.EntryCount <= 1);
    }

    [Fact(DisplayName = "Combined Fields: Union Statistics Exclude Deleted Documents")]
    public void CombinedFields_UnionStatistics_ExcludeDeletedDocuments()
    {
        // Every document matches; deleting half of them must change the DF, so scores rise.
        using var full = Build("pin_deletes_full", segments: 4, deleteEvery: 0);
        using var half = Build("pin_deletes_half", segments: 4, deleteEvery: 2);
        using var fullSearcher = new IndexSearcher(full);
        using var halfSearcher = new IndexSearcher(half);
        var query = new CombinedFieldsQuery(Fields, ["common"]);

        var fullResults = fullSearcher.Search(query, DocumentCount, TestContext.Current.CancellationToken);
        var halfResults = halfSearcher.Search(query, DocumentCount, TestContext.Current.CancellationToken);
        Assert.Equal(DocumentCount, fullResults.TotalHits);
        Assert.Equal(DocumentCount / 2, halfResults.TotalHits);
        Assert.All(halfResults.ScoreDocs, hit =>
            Assert.Equal(1, int.Parse(halfSearcher.GetStoredFields(hit.DocId)["id"][0], CultureInfo.InvariantCulture) % 2));
        Assert.True(halfResults.ScoreDocs[0].Score > fullResults.ScoreDocs[0].Score);
    }

    [Fact(DisplayName = "Combined Fields: Evicted Segment States Are Not Used After Release")]
    public void CombinedFields_EvictedSegmentStates_AreNotUsedAfterRelease()
    {
        using var directory = Build("pin_eviction", segments: 10, deleteEvery: 7);
        using var reference = new IndexSearcher(directory, new IndexSearcherConfig { MaxCachedSegmentReaders = 16 });
        using var evicting = new IndexSearcher(directory, new IndexSearcherConfig { MaxCachedSegmentReaders = 1 });

        var expected = Scores(reference, Search(reference));
        var actual = Scores(evicting, Search(evicting));
        Assert.Equal(expected, actual);
        Assert.True(evicting.SegmentReaderCacheMetrics.EntryCount <= 1);
        Assert.True(evicting.SegmentReaderCacheMetrics.EvictionCount > 0);
    }

    [Fact(DisplayName = "Combined Fields: Queries Stay Correct During Concurrent Refresh")]
    public async Task CombinedFields_QueriesStayCorrect_DuringConcurrentRefresh()
    {
        using var directory = Build("pin_refresh", segments: 3, deleteEvery: 0);
        int baseline = DocumentCount / 2;
        using var manager = new SearcherManager(directory);

        var writerTask = Task.Run(() =>
        {
            using var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 16, MergePolicy = NoMergePolicy.Instance });
            for (int round = 0; round < 20; round++)
            {
                for (int i = 0; i < 16; i++)
                {
                    var document = new LeanDocument();
                    document.Add(new StringField("id", "extra" + round.ToString(CultureInfo.InvariantCulture) + "_" + i.ToString(CultureInfo.InvariantCulture), stored: true));
                    document.Add(new TextField("title", "alpha common"));
                    document.Add(new TextField("body", "beta common"));
                    writer.AddDocument(document);
                }
                writer.Commit();
                manager.MaybeRefresh();
            }
        }, TestContext.Current.CancellationToken);

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!writerTask.IsCompleted)
            {
                using var lease = manager.AcquireLease();
                int hits = Search(lease.Searcher).TotalHits;
                // A snapshot sees the original matches plus whole committed batches of 16.
                Assert.InRange(hits, baseline, baseline + (20 * 16));
                Assert.Equal(0, (hits - baseline) % 16);
            }
        }, TestContext.Current.CancellationToken)).ToArray();

        await writerTask;
        await Task.WhenAll(readers);
        manager.MaybeRefresh();
        using var final = manager.AcquireLease();
        Assert.Equal(baseline + (20 * 16), Search(final.Searcher).TotalHits);
    }

    [Fact(DisplayName = "Combined Fields: Statistics Preparation Keeps To An Allocation Budget")]
    public void CombinedFields_StatisticsPreparation_KeepsToAllocationBudget()
    {
        using var directory = Build("pin_allocation", segments: 12, deleteEvery: 9);
        using var searcher = new IndexSearcher(directory);
        var query = new CombinedFieldsQuery(Fields, ["alpha", "beta", "common"]);

        // Warm the segment states and caches first.
        for (int i = 0; i < 3; i++)
            searcher.Search(query, 10, TestContext.Current.CancellationToken);

        long before = GC.GetAllocatedBytesForCurrentThread();
        searcher.Search(query, 10, TestContext.Current.CancellationToken);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // 12 segments x 3 terms x 2 fields; the budget does not grow with the number of postings.
        Assert.True(allocated < AllocationBudgetBytes, $"combined-fields search allocated {allocated} bytes");
    }

    // Measured at 27,608 bytes on net11.0 with one pin per segment; the budget allows about 1.8x.
    private const long AllocationBudgetBytes = 48 * 1024;

    private static int ThrowExpected() => throw new InvalidOperationException("expected");

    private static TopDocs Search(IndexSearcher searcher)
        => searcher.Search(new CombinedFieldsQuery(Fields, ["alpha", "beta"], minimumShouldMatch: 2), DocumentCount * 2, TestContext.Current.CancellationToken);

    private static SortedDictionary<string, float> Scores(IndexSearcher searcher, TopDocs results)
    {
        var scores = new SortedDictionary<string, float>(StringComparer.Ordinal);
        foreach (var hit in results.ScoreDocs)
            scores.Add(searcher.GetStoredFields(hit.DocId)["id"][0], hit.Score);
        return scores;
    }

    // Even documents have "alpha" in the title and "beta" in the body; odd ones swap in other terms.
    // Every document contains "common".
    private MMapDirectory Build(string name, int segments, int deleteEvery)
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
            bool even = i % 2 == 0;
            document.Add(new TextField("title", (even ? "alpha common" : "gamma common") + (i % 3 == 0 ? " alpha" : string.Empty)));
            document.Add(new TextField("body", even ? "beta common delta" : "delta common"));
            writer.AddDocument(document);
        }
        writer.Commit();
        if (deleteEvery > 0)
        {
            for (int i = 0; i < DocumentCount; i += deleteEvery)
                writer.DeleteDocuments(new TermQuery("id", i.ToString(CultureInfo.InvariantCulture)));
            writer.Commit();
        }
        return directory;
    }
}

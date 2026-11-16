using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search.Aggregations;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Search.Aggregations;

/// <summary>
/// Post-hoc <see cref="NumericAggregator.Aggregate(ReadOnlySpan{int}, AggregationRequest[], IReadOnlyList{Index.Segment.SegmentReader}, int[], int)"/>
/// over many segments and many requests: scratch is sized by the segments that hold matching
/// documents and the distinct fields requested, not by every segment and request pair.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class NumericAggregatorPostHocTests : IDisposable
{
    private const int SegmentCount = 200;
    private const int RequestCount = 60;
    private readonly string _dir = RepositoryTemporaryStorage.NewPath("tests", "agg_posthoc_");

    public NumericAggregatorPostHocTests()
    {
        Directory.CreateDirectory(_dir);
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig
        {
            MaxBufferedDocs = 1,
            MergePolicy = NoMergePolicy.Instance,
            DurableCommits = false,
        });
        for (int i = 0; i < SegmentCount; i++)
        {
            var document = new LeanDocument();
            document.Add(new TextField("name", "product"));
            document.Add(new NumericField("price", 10.0 + i));
            document.Add(new NumericField("rating", i % 5 + 1.0));
            writer.AddDocument(document);
        }
        writer.Commit();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch (IOException) { /* mmap handles may linger on Windows */ }
        catch (UnauthorizedAccessException) { /* as above */ }
    }

    private static AggregationRequest[] Requests() => Enumerable.Range(0, RequestCount)
        .Select(static i => new AggregationRequest($"agg{i}", i % 2 == 0 ? "price" : "rating",
            i % 3 == 0 ? AggregationType.Histogram : AggregationType.Stats) { HistogramInterval = 10 })
        .ToArray();

    [Fact(DisplayName = "Post-Hoc Aggregation: Empty And Sparse Hits Over Many Segments Allocate Little Scratch")]
    public void EmptyAndSparseHits_AllocateLittleScratch()
    {
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var readers = searcher.GetSegmentReaders();
        Assert.Equal(SegmentCount, readers.Count);
        int[] docBases = readers.Select(static reader => reader.DocBase).ToArray();
        int total = docBases[^1] + readers[^1].MaxDoc;
        var requests = Requests();
        int[] sparse = [docBases[3], docBases[97], docBases[198]];

        for (int warmup = 0; warmup < 3; warmup++)
        {
            NumericAggregator.Aggregate(ReadOnlySpan<int>.Empty, requests, readers, docBases, total);
            NumericAggregator.Aggregate(sparse, requests, readers, docBases, total);
        }

        long emptyBytes = Measure(() => NumericAggregator.Aggregate(ReadOnlySpan<int>.Empty, requests, readers, docBases, total));
        long sparseBytes = Measure(() => NumericAggregator.Aggregate(sparse, requests, readers, docBases, total));

        // Resolving every request separately and keeping a slot for every segment and request pair
        // allocated about 3.5 MB here, even with no hits. Resolving each distinct field once across
        // the 200 segments, plus states and results, stays near 130 kB.
        Assert.True(emptyBytes < 256 * 1024, $"Empty post-hoc aggregation allocated {emptyBytes} bytes.");
        Assert.True(sparseBytes - emptyBytes < 16 * 1024,
            $"Three sparse hits allocated {sparseBytes} bytes against {emptyBytes} for none.");
    }

    [Fact(DisplayName = "Post-Hoc Aggregation: Interleaved Hits Match The Streaming Collector")]
    public void InterleavedHits_MatchStreamingCollector()
    {
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var readers = searcher.GetSegmentReaders();
        int[] docBases = readers.Select(static reader => reader.DocBase).ToArray();
        int total = docBases[^1] + readers[^1].MaxDoc;
        var requests = Requests();

        // Every document, visiting segments out of order and twice over the same field per document.
        int[] interleaved = Enumerable.Range(0, total).OrderBy(static id => (id * 7919) % 211).ToArray();
        var posthoc = NumericAggregator.Aggregate(interleaved, requests, readers, docBases, total);
        var (_, streaming) = searcher.SearchWithAggregations(new TermQuery("name", "product"), total, requests);

        Assert.Equal(streaming.Length, posthoc.Length);
        for (int i = 0; i < posthoc.Length; i++)
        {
            Assert.Equal(streaming[i].Name, posthoc[i].Name);
            Assert.Equal(streaming[i].Count, posthoc[i].Count);
            Assert.Equal(streaming[i].Min, posthoc[i].Min);
            Assert.Equal(streaming[i].Max, posthoc[i].Max);
            Assert.Equal(streaming[i].Sum, posthoc[i].Sum, 9);
            Assert.Equal(
                streaming[i].Buckets?.Select(static b => (b.LowerBound, b.Count)) ?? [],
                posthoc[i].Buckets?.Select(static b => (b.LowerBound, b.Count)) ?? []);
        }
    }

    private static long Measure(Action action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}

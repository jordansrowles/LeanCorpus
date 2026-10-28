using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>Regression coverage for ordered batched replacement updates.</summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class IndexWriterBulkUpdateTests : IClassFixture<TestDirectoryFixture>
{
    private const int ReplacementCount = 100;
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public IndexWriterBulkUpdateTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact(DisplayName = "Replacement Batch: Applies Deletes Once And Forms One Segment")]
    public void ReplacementBatch_AppliesDeletesOnceAndFormsOneSegment()
    {
        string oldPath = NewPath("unbatched");
        string batchPath = NewPath("batched");
        CountingMetricsCollector oldMetrics = new();
        CountingMetricsCollector batchMetrics = new();
        int oldSegmentGrowth;
        int batchSegmentGrowth;

        using (var oldDirectory = new MMapDirectory(oldPath))
        using (var oldWriter = new IndexWriter(oldDirectory, CreateConfig(oldMetrics)))
        {
            oldWriter.AddDocuments(Enumerable.Range(0, ReplacementCount)
                .Select(static id => CreateDocument(id, "original"))
                .ToArray());
            oldWriter.Commit();
            int segmentsBefore = oldWriter.GetNrtSegments().Count;

            for (int id = 0; id < ReplacementCount; id++)
                oldWriter.UpdateDocument("id", $"doc-{id}", CreateDocument(id, "replacement"));

            oldWriter.Commit();
            oldSegmentGrowth = oldWriter.GetNrtSegments().Count - segmentsBefore;
        }

        using (var batchDirectory = new MMapDirectory(batchPath))
        using (var batchWriter = new IndexWriter(batchDirectory, CreateConfig(batchMetrics)))
        {
            batchWriter.AddDocuments(Enumerable.Range(0, ReplacementCount)
                .Select(static id => CreateDocument(id, "original"))
                .ToArray());
            batchWriter.Commit();
            int segmentsBefore = batchWriter.GetNrtSegments().Count;

            (string Term, LeanDocument Replacement)[] replacements = Enumerable.Range(0, ReplacementCount)
                .Select(static id => ($"doc-{id}", CreateDocument(id, "replacement")))
                .ToArray();
            batchWriter.UpdateDocuments("id", replacements);
            batchWriter.Commit();
            batchSegmentGrowth = batchWriter.GetNrtSegments().Count - segmentsBefore;
        }

        _output.WriteLine(
            $"Repeated UpdateDocument: {oldMetrics.DeleteApplicationCount} deletion passes, {oldSegmentGrowth} segments added; batch: {batchMetrics.DeleteApplicationCount} deletion pass, {batchSegmentGrowth} segment added.");

        Assert.Equal(ReplacementCount, oldMetrics.DeleteApplicationCount);
        Assert.Equal(ReplacementCount, oldMetrics.DeleteTermCount);
        Assert.Equal(ReplacementCount, oldMetrics.ChangedSegmentCount);
        Assert.Equal(ReplacementCount, oldSegmentGrowth);

        Assert.Equal(1, batchMetrics.DeleteApplicationCount);
        Assert.Equal(ReplacementCount, batchMetrics.DeleteTermCount);
        Assert.Equal(1, batchMetrics.ChangedSegmentCount);
        Assert.Equal(1, batchSegmentGrowth);
    }

    private string NewPath(string suffix)
    {
        string path = Path.Combine(_fixture.Path, $"bulk-update-{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static IndexWriterConfig CreateConfig(IMetricsCollector metrics) => new()
    {
        MaxBufferedDocs = 1_000,
        MergePolicy = NoMergePolicy.Instance,
        Metrics = metrics,
    };

    private static LeanDocument CreateDocument(int id, string body)
    {
        LeanDocument document = new();
        document.Add(new StringField("id", $"doc-{id}"));
        document.Add(new TextField("body", body));
        return document;
    }

    private sealed class CountingMetricsCollector : IMetricsCollector
    {
        public int DeleteApplicationCount { get; private set; }
        public int DeleteTermCount { get; private set; }
        public int ChangedSegmentCount { get; private set; }

        public void RecordSearchLatency(TimeSpan elapsed) { }
        public void RecordCacheHit() { }
        public void RecordCacheMiss() { }
        public void RecordFlush(TimeSpan elapsed) { }
        public void RecordMerge(TimeSpan elapsed, int segmentsMerged) { }
        public void RecordCommit(TimeSpan elapsed) { }

        public void RecordDeleteApplication(TimeSpan elapsed, int terms, int changedSegments)
        {
            DeleteApplicationCount++;
            DeleteTermCount += terms;
            ChangedSegmentCount += changedSegments;
        }

        public MetricsSnapshot GetSnapshot() => MetricsSnapshot.Empty;
    }
}

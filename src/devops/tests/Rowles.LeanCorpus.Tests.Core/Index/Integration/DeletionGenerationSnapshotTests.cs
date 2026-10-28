using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class DeletionGenerationSnapshotTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public DeletionGenerationSnapshotTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Repeated uncommitted updates preserve an earlier reader's deletion state")]
    public void RepeatedUpdates_PreserveEarlierReaderDeletionState()
    {
        using var directory = new MMapDirectory(Path.Combine(_fixture.Path, nameof(RepeatedUpdates_PreserveEarlierReaderDeletionState)));
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance });
        writer.AddDocument(Document("first"));
        writer.AddDocument(Document("second"));
        writer.Commit();

        writer.UpdateDocument("id", "first", Document("first"));
        SegmentInfo original = writer.GetNrtSegments()[0];
        using var earlierReader = new SegmentReader(directory, original);

        // Keep liveness cold until the same source segment receives another delete.
        writer.UpdateDocument("id", "second", Document("second"));

        Assert.False(earlierReader.IsLive(0));
        Assert.True(earlierReader.IsLive(1));
        writer.Commit();
        Assert.True(earlierReader.IsLive(1));
        using var searcher = new IndexSearcher(directory);
        Assert.Equal(2, searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact(DisplayName = "Rollback does not reuse a deletion file still selected by an earlier reader")]
    public void Rollback_PreservesEarlierReaderDeletionState()
    {
        using var directory = new MMapDirectory(Path.Combine(_fixture.Path, nameof(Rollback_PreservesEarlierReaderDeletionState)));
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance });
        writer.AddDocument(Document("first"));
        writer.AddDocument(Document("second"));
        writer.Commit();

        writer.UpdateDocument("id", "first", Document("first"));
        using var earlierReader = new SegmentReader(directory, writer.GetNrtSegments()[0]);
        writer.Rollback();
        writer.UpdateDocument("id", "second", Document("second"));

        Assert.False(earlierReader.IsLive(0));
        Assert.True(earlierReader.IsLive(1));
    }

    [Fact(DisplayName = "A background merge cannot publish documents deleted after its source snapshot")]
    public async Task BackgroundMerge_DiscardsOutputAfterSourceChanges()
    {
        var policy = new OnceEnabledMergePolicy();
        var metrics = new MergeBacklogCallback();
        using var directory = new MMapDirectory(Path.Combine(_fixture.Path, nameof(BackgroundMerge_DiscardsOutputAfterSourceChanges)));
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MergePolicy = policy,
            MaxBufferedDocs = 1,
            Metrics = metrics
        });
        writer.AddDocument(Document("first"));
        writer.AddDocument(Document("second"));
        writer.Commit();

        // This production metrics boundary follows source capture and precedes execution.
        metrics.Callback = () => writer.UpdateDocument("id", "first", Document("updated"));
        policy.Enabled = true;
        MergeScheduler.ScheduleBackgroundMerge(writer);
        Assert.NotNull(writer.MergeTask);
        await writer.MergeTask.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        writer.Commit();

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(0, searcher.Search(new TermQuery("id", "first"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "updated"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "second"), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    private sealed class OnceEnabledMergePolicy : IMergePolicy
    {
        public bool Enabled { get; set; }

        public IReadOnlyList<SegmentInfo> FindMerges(IReadOnlyList<SegmentInfo> segments, IReadOnlySet<string> protectedSegmentIds)
        {
            if (!Enabled) return [];
            Enabled = false;
            return segments.Where(segment => !protectedSegmentIds.Contains(segment.SegmentId)).ToArray();
        }
    }

    private sealed class MergeBacklogCallback : IMetricsCollector
    {
        public Action? Callback { get; set; }
        public void RecordMergeBacklog(long pendingBytes, TimeSpan producerStall) => Callback?.Invoke();
        public void RecordSearchLatency(TimeSpan elapsed) { }
        public void RecordCacheHit() { }
        public void RecordCacheMiss() { }
        public void RecordFlush(TimeSpan elapsed) { }
        public void RecordMerge(TimeSpan elapsed, int segmentsMerged) { }
        public void RecordCommit(TimeSpan elapsed) { }
        public MetricsSnapshot GetSnapshot() => MetricsSnapshot.Empty;
    }

    private static LeanDocument Document(string id)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        return document;
    }
}

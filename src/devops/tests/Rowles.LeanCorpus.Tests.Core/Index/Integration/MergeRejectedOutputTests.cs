using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Deterministic background-merge checks for the three non-adoption outcomes: cancellation before merge
/// work, cancellation after completed output but before adoption, and completed output rejected because
/// a source segment changed. Each must keep the previous commit readable, publish nothing, release source
/// reservations and leave no destination segment files behind.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class MergeRejectedOutputTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public MergeRejectedOutputTests(TestDirectoryFixture fixture) => _fixture = fixture;

    public static TheoryData<bool> CompoundModes => new() { false, true };

    [Theory(DisplayName = "Cancellation before merge work starts leaves no destination output")]
    [MemberData(nameof(CompoundModes))]
    public async Task CancellationBeforeMergeWork_LeavesNoDestination(bool useCompoundFile)
    {
        var policy = new OnceEnabledMergePolicy();
        var metrics = new MergeBacklogCallback();
        using var directory = new MMapDirectory(NewPath(nameof(CancellationBeforeMergeWork_LeavesNoDestination), useCompoundFile));
        int mergesStarted = 0;
        var writer = new IndexWriter(directory, Config(policy, metrics, useCompoundFile));
        writer.Config.MergeStarting = () => Interlocked.Increment(ref mergesStarted);
        try
        {
            IndexSources(writer);
            var committedIds = SegmentIds(writer);

            // This boundary follows source capture and precedes the task's first cancellation check.
            metrics.Callback = writer.MergeCts.Cancel;
            policy.Enabled = true;
            MergeScheduler.ScheduleBackgroundMerge(writer);
            Assert.NotNull(writer.MergeTask);
            await writer.MergeTask.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            Assert.Equal(0, mergesStarted);
            AssertNotAdopted(writer, committedIds);
            AssertNoOrphanSegmentFiles(directory, writer);
        }
        finally
        {
            writer.Dispose();
        }

        AssertCommittedSourcesReadable(directory);
    }

    [Theory(DisplayName = "Cancellation after completed merge output and before adoption removes the destination")]
    [MemberData(nameof(CompoundModes))]
    public async Task CancellationBeforeAdoption_RemovesCompletedDestination(bool useCompoundFile)
    {
        var policy = new OnceEnabledMergePolicy();
        using var directory = new MMapDirectory(NewPath(nameof(CancellationBeforeAdoption_RemovesCompletedDestination), useCompoundFile));
        var writer = new IndexWriter(directory, Config(policy, metrics: null, useCompoundFile));
        try
        {
            IndexSources(writer);
            var committedIds = SegmentIds(writer);

            // The merge has passed its start check; cancelling here leaves only the adoption check.
            writer.Config.MergeStarting = writer.MergeCts.Cancel;
            policy.Enabled = true;
            MergeScheduler.ScheduleBackgroundMerge(writer);
            Assert.NotNull(writer.MergeTask);
            await writer.MergeTask.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            AssertNotAdopted(writer, committedIds);
            AssertNoOrphanSegmentFiles(directory, writer);
        }
        finally
        {
            writer.Dispose();
        }

        AssertCommittedSourcesReadable(directory);
    }

    [Theory(DisplayName = "Completed merge output rejected after a source change is removed and not published")]
    [MemberData(nameof(CompoundModes))]
    public async Task StaleSourceRejection_RemovesCompletedDestination(bool useCompoundFile)
    {
        var policy = new OnceEnabledMergePolicy();
        using var directory = new MMapDirectory(NewPath(nameof(StaleSourceRejection_RemovesCompletedDestination), useCompoundFile));
        using var writer = new IndexWriter(directory, Config(policy, metrics: null, useCompoundFile));
        IndexSources(writer);
        var committedIds = SegmentIds(writer);

        // Change a source's deletion state after source capture and before adoption.
        writer.Config.MergeStarting = () => writer.UpdateDocument("id", "first", Document("updated"));
        policy.Enabled = true;
        MergeScheduler.ScheduleBackgroundMerge(writer);
        Assert.NotNull(writer.MergeTask);
        await writer.MergeTask.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Empty(writer.ReservedMergeSegments);
        Assert.Empty(writer.ObsoleteMergeSegments);
        Assert.All(committedIds, id => Assert.Contains(id, SegmentIds(writer)));

        // Commit publishes the update's flushed segment, so every remaining segment file is then owned.
        writer.Commit();
        AssertNoOrphanSegmentFiles(directory, writer);
        using var searcher = new IndexSearcher(directory);
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(0, searcher.Search(new TermQuery("id", "first"), 10, ct).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "updated"), 10, ct).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "second"), 10, ct).TotalHits);
    }

    private string NewPath(string name, bool useCompoundFile)
        => Path.Combine(_fixture.Path, $"{name}-{(useCompoundFile ? "compound" : "loose")}");

    private static IndexWriterConfig Config(IMergePolicy policy, IMetricsCollector? metrics, bool useCompoundFile)
    {
        var config = new IndexWriterConfig
        {
            MergePolicy = policy,
            MaxBufferedDocs = 1,
            UseCompoundFile = useCompoundFile,
        };
        if (metrics is not null)
            config.Metrics = metrics;
        return config;
    }

    private static void IndexSources(IndexWriter writer)
    {
        writer.AddDocument(Document("first"));
        writer.AddDocument(Document("second"));
        writer.Commit();
        Assert.True(writer.CommittedSegments.Count >= 2);
    }

    private static HashSet<string> SegmentIds(IndexWriter writer)
        => writer.CommittedSegments.Select(static segment => segment.SegmentId).ToHashSet(StringComparer.Ordinal);

    private static void AssertNotAdopted(IndexWriter writer, HashSet<string> committedIds)
    {
        Assert.Empty(writer.ReservedMergeSegments);
        Assert.Empty(writer.ObsoleteMergeSegments);
        Assert.False(writer.ContentChangedSinceCommit);
        Assert.Equal(committedIds, SegmentIds(writer));
    }

    /// <summary>Every segment-owned file must belong to a segment the writer still tracks.</summary>
    private static void AssertNoOrphanSegmentFiles(MMapDirectory directory, IndexWriter writer)
    {
        var known = SegmentIds(writer);
        var files = System.IO.Directory.EnumerateFiles(directory.DirectoryPath)
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToArray();
        var orphans = SegmentFileSet.FindSegmentIds(files)
            .Where(id => !known.Contains(id))
            .SelectMany(id => files.Where(name => SegmentReader.IsSegmentFile(id, name)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(orphans.Length == 0, "Unowned destination files: " + string.Join(", ", orphans));
    }

    private static void AssertCommittedSourcesReadable(MMapDirectory directory)
    {
        using var searcher = new IndexSearcher(directory);
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(1, searcher.Search(new TermQuery("id", "first"), 10, ct).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "second"), 10, ct).TotalHits);
        Assert.Equal(2, searcher.Search(new MatchAllDocsQuery(), 10, ct).TotalHits);
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

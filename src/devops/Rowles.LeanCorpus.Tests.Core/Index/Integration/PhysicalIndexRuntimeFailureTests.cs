namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class PhysicalIndexRuntimeFailureTests
{
    [Fact]
    public async Task RequestLocalRejectionLeavesWriterAndSearchAvailable()
    {
        string path = NewPath();
        var runtime = Open(path, new IndexWriterConfig { DurableCommits = false });
        try
        {
            using (runtime.EnterOperation())
                Assert.Throws<ArgumentNullException>(() => runtime.Writer.AddDocument(null!));
            Assert.Equal(PhysicalIndexRuntimeHealth.Healthy, runtime.GetDiagnostics().Health);
            Assert.True(runtime.GetDiagnostics().WritesAllowed);
            using (runtime.EnterOperation()) runtime.Writer.AddDocument(Document("accepted"));
            runtime.Commit(refresh: true);
            using var lease = runtime.AcquireSearcher();
            Assert.Equal(1, lease.Searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
        }
        finally
        {
            await runtime.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task FailedCommitBeforePublicationIsRetryableWithoutPoisoningWriter()
    {
        string path = NewPath();
        var failure = new IOException("controlled pre-publication failure");
        var config = new IndexWriterConfig { DurableCommits = false, CommitBeforePublication = _ => throw failure };
        var runtime = Open(path, config);
        try
        {
            runtime.Writer.AddDocument(Document("retry"));
            Assert.Same(failure, Assert.Throws<IOException>(() => runtime.Commit(refresh: false)));
            var diagnostic = runtime.GetDiagnostics();
            Assert.Equal(PhysicalIndexRuntimeHealth.Degraded, diagnostic.Health);
            Assert.True(diagnostic.WritesAllowed);
            Assert.False(diagnostic.ReopenRequired);
            Assert.Equal("commit", diagnostic.LastFailure?.Component);
            config.CommitBeforePublication = null;
            runtime.Commit(refresh: true);
            Assert.Equal(PhysicalIndexRuntimeHealth.Healthy, runtime.GetDiagnostics().Health);
            using var lease = runtime.AcquireSearcher();
            Assert.Equal(1, lease.Searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
        }
        finally
        {
            await runtime.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task FailedDurabilityAfterPublicationRequiresANewOwnerAndPreservesOldSearchView()
    {
        string path = NewPath();
        var failure = new IOException("controlled publication durability failure");
        var runtime = Open(path, new IndexWriterConfig
        {
            DurableCommits = true,
            PreparedCommitPublicationSync = _ => throw failure,
        });
        try
        {
            runtime.Writer.AddDocument(Document("published"));
            runtime.Writer.PrepareCommit();
            Assert.Same(failure, Assert.Throws<IOException>(() => runtime.Commit(refresh: true)));
            var diagnostic = runtime.GetDiagnostics();
            Assert.Equal(PhysicalIndexRuntimeHealth.Faulted, diagnostic.Health);
            Assert.False(diagnostic.WritesAllowed);
            Assert.True(diagnostic.ReopenRequired);
            Assert.Same(failure, diagnostic.LastFailure?.Exception);
            Assert.Throws<InvalidOperationException>(() => runtime.EnterOperation());
            using (var old = runtime.AcquireSearcher())
                Assert.Equal(0, old.Searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
            await runtime.CloseAsync();
            await using var reopened = Open(path, new IndexWriterConfig { DurableCommits = false });
            using var current = reopened.AcquireSearcher();
            Assert.Equal(1, current.Searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
        }
        finally
        {
            await runtime.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshFailureRetainsOldGenerationAndCanRecover()
    {
        string path = NewPath();
        var runtime = Open(path, new IndexWriterConfig { DurableCommits = false });
        var commits = new Dictionary<string, byte[]>();
        try
        {
            runtime.Writer.AddDocument(Document("retained"));
            runtime.Commit(refresh: true);
            foreach (string commit in Directory.EnumerateFiles(path, "segments_*"))
            {
                commits.Add(commit, File.ReadAllBytes(commit));
                File.WriteAllText(commit, "controlled corrupt commit");
            }
            Assert.False(runtime.Refresh());
            var diagnostic = runtime.GetDiagnostics();
            Assert.Equal(PhysicalIndexRuntimeHealth.Degraded, diagnostic.Health);
            Assert.Equal("refresh", diagnostic.LastFailure?.Component);
            Assert.True(diagnostic.WritesAllowed);
            using (var old = runtime.AcquireSearcher())
                Assert.Equal(1, old.Searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
            foreach (var commit in commits) File.WriteAllBytes(commit.Key, commit.Value);
            runtime.Refresh();
            Assert.Equal(PhysicalIndexRuntimeHealth.Healthy, runtime.GetDiagnostics().Health);
        }
        finally
        {
            foreach (var commit in commits) File.WriteAllBytes(commit.Key, commit.Value);
            await runtime.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task FailedFlushDuringCloseReleasesResourcesButNeverReportsClosed()
    {
        string path = NewPath();
        var failure = new IOException("controlled physical flush failure");
        using var completed = new ManualResetEventSlim();
        var runtime = Open(path, new IndexWriterConfig
        {
            DurableCommits = false,
            MaxBufferedDocs = 1,
            PhysicalFlushStarted = () => throw failure,
            PhysicalFlushCompleted = completed.Set,
        });
        try
        {
            runtime.Writer.AddDocument(Document("failed"));
            Assert.True(completed.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => runtime.CloseAsync().AsTask()));
            var diagnostic = runtime.GetDiagnostics();
            Assert.Equal(PhysicalIndexRuntimeState.Faulted, diagnostic.State);
            Assert.Same(failure, diagnostic.LastCloseFailure);
            Assert.False(diagnostic.WriterOwned);
            Assert.False(diagnostic.DirectoryOwned);
            Assert.Throws<ObjectDisposedException>(() => runtime.AcquireSearcher());
            using (File.Open(Path.Combine(path, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
        }
        finally
        {
            try { await runtime.DisposeAsync(); } catch (IOException) { }
            Directory.Delete(path, recursive: true);
        }
    }

    private static PhysicalIndexRuntime Open(string path, IndexWriterConfig config) => new(path, config,
        new SearcherManagerConfig { RefreshInterval = TimeSpan.FromHours(1) });
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "leancorpus-runtime-failures", Guid.NewGuid().ToString("N"));
    private static LeanDocument Document(string value)
    {
        var document = new LeanDocument();
        document.Add(new TextField("body", value));
        return document;
    }
}

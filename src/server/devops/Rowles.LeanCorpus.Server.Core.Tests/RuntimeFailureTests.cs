using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Core.Execution;

namespace Rowles.LeanCorpus.Server.Core.Tests;

[Trait("Area", "Server")]
public sealed class RuntimeFailureTests
{
    [Fact]
    public async Task FatalPublicationFailureDegradesTheHostFailsWaitersAndPreservesTheOldReader()
    {
        string path = NewPath();
        var handle = OpenHandle(path);
        var runtime = handle.Runtime;
        var failure = new IOException("controlled post-publication durability failure");
        try
        {
            runtime.Writer.AddDocument(Document());
            long sequence = runtime.MarkWrite();
            runtime.Writer.PrepareCommit();
            runtime.Writer.Config.PreparedCommitPublicationSync = _ => throw failure;
            Task<LocalCommitReceipt> waiting = runtime.Commits.WaitUntilCommittedAsync(sequence, TestContext.Current.CancellationToken).AsTask();
            Assert.True(runtime.Commits.Commit() is CommitFailed);
            Assert.True(handle.Health.IsDegraded);
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)));
            await Assert.ThrowsAsync<IOException>(async () => await runtime.Commits.WaitUntilCommittedAsync(sequence + 1, TestContext.Current.CancellationToken));
            using var operation = await handle.EnterOperationAsync(TestContext.Current.CancellationToken);
            using var old = runtime.Searchers.AcquireLease();
            Assert.Equal(0, old.Searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
        }
        finally
        {
            runtime.Writer.Config.PreparedCommitPublicationSync = null;
            try { await handle.DisposeAsync(); } catch { /* The failed sequence remains failed during close. */ }
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task RetryableCommitFailureRemainsDegradedUntilRetryAndThenCompletesItsWaiter()
    {
        string path = NewPath();
        var handle = OpenHandle(path);
        var runtime = handle.Runtime;
        try
        {
            runtime.Writer.AddDocument(Document());
            long sequence = runtime.MarkWrite();
            runtime.Writer.Config.CommitBeforePublication = _ => throw new IOException("controlled pre-publication failure");
            Task<LocalCommitReceipt> waiting = runtime.Commits.WaitUntilCommittedAsync(sequence, TestContext.Current.CancellationToken).AsTask();
            Assert.True(runtime.Commits.Commit() is CommitFailed);
            Assert.True(handle.Health.IsDegraded);
            Assert.False(waiting.IsCompleted);
            runtime.Writer.Config.CommitBeforePublication = null;
            Assert.True(runtime.Commits.Commit(refresh: true) is CommitPublished);
            Assert.True((await waiting.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)).IsDurable);
            Assert.False(handle.Health.IsDegraded);
        }
        finally
        {
            runtime.Writer.Config.CommitBeforePublication = null;
            await handle.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task RecoverableFailureCannotOverwriteALaterSuccessfulReceiptOrHealth()
    {
        string path = NewPath();
        var handle = OpenHandle(path);
        var runtime = handle.Runtime;
        var firstCommitEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCommitAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        try
        {
            runtime.Writer.AddDocument(Document());
            long sequence = runtime.MarkWrite();
            Task<LocalCommitReceipt> waiter = runtime.Commits.WaitUntilCommittedAsync(sequence, TestContext.Current.CancellationToken).AsTask();
            runtime.Writer.Config.CommitBeforePublication = _ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    firstCommitEntered.TrySetResult();
                    releaseFirstCommit.Task.GetAwaiter().GetResult();
                    throw new IOException("controlled recoverable first-attempt failure");
                }
            };

            Task<CommitResult> first = Task.Run(() => runtime.Commits.Commit());
            await firstCommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task<CommitResult> second = Task.Run(() =>
            {
                secondCommitAttempted.TrySetResult();
                return runtime.Commits.Commit(refresh: true);
            });
            await secondCommitAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(second.IsCompleted);

            releaseFirstCommit.TrySetResult();
            Assert.True(await first is CommitFailed);
            Assert.True(await second is CommitPublished);
            LocalCommitReceipt receipt = await waiter.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(sequence, receipt.LastSequenceNumber);
            Assert.Equal(receipt, runtime.Commits.LastReceipt);
            Assert.Null(runtime.Commits.LastFailure);
            Assert.Equal(0, runtime.Commits.ConsecutiveFailures);
            Assert.False(handle.Health.IsDegraded);
        }
        finally
        {
            releaseFirstCommit.TrySetResult();
            runtime.Writer.Config.CommitBeforePublication = null;
            await handle.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshFailureDegradesHostHealthWhileTheOldGenerationRemainsUsable()
    {
        string path = NewPath();
        var handle = OpenHandle(path);
        var saved = new Dictionary<string, byte[]>();
        try
        {
            handle.Runtime.Writer.AddDocument(Document());
            handle.Runtime.MarkWrite();
            Assert.True(handle.Runtime.Commits.Commit(refresh: true) is CommitPublished);
            foreach (string commit in Directory.EnumerateFiles(path, "segments_*"))
            {
                saved.Add(commit, File.ReadAllBytes(commit));
                File.WriteAllText(commit, "controlled corrupt commit");
            }
            Assert.False(handle.Runtime.Searchers.Refresh());
            Assert.True(handle.Health.IsDegraded);
            Assert.True(handle.Health.IsUsable);
            foreach (var commit in saved) File.WriteAllBytes(commit.Key, commit.Value);
            handle.Runtime.Refresh();
            Assert.False(handle.Health.IsDegraded);
        }
        finally
        {
            foreach (var commit in saved) File.WriteAllBytes(commit.Key, commit.Value);
            await handle.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task DurableReceiptDoesNotClaimVisibilityWhenTheRequiredRefreshFails()
    {
        string path = NewPath();
        var handle = OpenHandle(path);
        var runtime = handle.Runtime;
        string? commitPath = null;
        byte[]? committedBytes = null;
        try
        {
            runtime.Writer.AddDocument(Document());
            long sequence = runtime.MarkWrite();
            runtime.Writer.PrepareCommit();
            runtime.Writer.Config.PreparedCommitPublicationSync = directory =>
            {
                commitPath = Path.Combine(directory, $"segments_{runtime.Writer.CurrentCommitGeneration}");
                committedBytes = File.ReadAllBytes(commitPath);
                File.WriteAllText(commitPath, "controlled post-commit refresh failure");
            };
            Task<LocalCommitReceipt> waiting = runtime.Commits.WaitUntilCommittedAsync(sequence, TestContext.Current.CancellationToken).AsTask();
            CommitResult result = runtime.Commits.Commit(refresh: true);
            Assert.True(result is CommitPublished);
            var receipt = result switch { CommitPublished published => published.Receipt, _ => throw new InvalidOperationException() };
            Assert.True(receipt.IsDurable);
            Assert.False(receipt.IsVisible);
            Assert.True(handle.Health.IsDegraded);
            Assert.Equal(receipt, await waiting.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            Assert.ThrowsAny<Exception>(runtime.Refresh);
            File.WriteAllBytes(commitPath!, committedBytes!);
            runtime.Refresh();
            Assert.False(handle.Health.IsDegraded);
            using var visible = runtime.Searchers.AcquireLease();
            Assert.Equal(receipt.ContentToken, visible.ContentToken);
        }
        finally
        {
            runtime.Writer.Config.PreparedCommitPublicationSync = null;
            if (commitPath is not null && committedBytes is not null) File.WriteAllBytes(commitPath, committedBytes);
            await handle.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task UnexpectedRefreshExceptionAfterPhysicalCommitDoesNotHideDurableReceipt()
    {
        string path = NewPath();
        var handle = OpenHandle(path);
        var runtime = handle.Runtime;
        var refreshFailure = new InvalidOperationException("controlled unexpected post-commit refresh failure");
        try
        {
            runtime.Writer.AddDocument(Document());
            long sequence = runtime.MarkWrite();
            Task<LocalCommitReceipt> waiting = runtime.Commits.WaitUntilCommittedAsync(sequence, TestContext.Current.CancellationToken).AsTask();
            runtime.BeforeRefreshForTesting = () => throw refreshFailure;

            CommitResult result = runtime.Commits.Commit(refresh: true);

            Assert.Equal(0, runtime.PendingOperations);
            Assert.True(runtime.Writer.CurrentCommitGeneration > 0);
            if (result is CommitFailed failed)
                Assert.Same(refreshFailure, failed.Exception);
            Assert.True(result is CommitPublished, $"Expected a durable receipt after the physical commit, but received {result}.");
            var published = result switch { CommitPublished value => value, _ => throw new InvalidOperationException() };
            Assert.True(published.Receipt.IsDurable);
            Assert.False(published.Receipt.IsVisible);
            Assert.True(waiting.IsCompletedSuccessfully, "A durable post-commit receipt must complete its sequence waiter.");
            Assert.Equal(published.Receipt, await waiting);
            Assert.Same(refreshFailure, runtime.LastRefreshFailure);
            Assert.True(handle.Health.IsDegraded);

            runtime.BeforeRefreshForTesting = null;
            runtime.Refresh();
            Assert.Null(runtime.LastRefreshFailure);
            Assert.False(handle.Health.IsDegraded);
            using var visible = runtime.Searchers.AcquireLease();
            Assert.Equal(1, visible.Searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
        }
        finally
        {
            runtime.BeforeRefreshForTesting = null;
            await handle.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    private static LeanDocument Document()
    {
        var document = new LeanDocument();
        document.Add(new TextField("body", "retained"));
        return document;
    }
    private static LocalIndexHandle OpenHandle(string path) => new(new LocalIndexDescriptor(PhysicalIndexId.New(),
        new IndexSchema([new IndexFieldDefinition("body", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
        "failure-schema", new MutableIndexSettings(null, null, "body", null)), path, TimeSpan.FromHours(1), TimeSpan.FromHours(1), LocalIndexOpenMode.ReadWrite);
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "leancorpus-server-runtime-failures", Guid.NewGuid().ToString("N"));
}

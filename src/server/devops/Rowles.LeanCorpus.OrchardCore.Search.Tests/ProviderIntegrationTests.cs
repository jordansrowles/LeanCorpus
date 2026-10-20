using OrchardCore.Indexing;
using OrchardCore.Locking;
using OrchardCore.Locking.Distributed;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.OrchardCore.Search.Indexing;
using Rowles.LeanCorpus.OrchardCore.Search.Search;
using Rowles.LeanCorpus.OrchardCore.Search.Storage;

namespace Rowles.LeanCorpus.OrchardCore.Search.Tests;

[Trait("Area", "Orchard")]
public sealed class ProviderIntegrationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task QueuedCacheOperationsCannotOpenHandlesAfterTheirOwnerIsDisposed(int operationKind)
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        LeanCorpusIndexPaths paths = fixture.Paths.Resolve(fixture.Profile.IndexFullName);
        LeanCorpusSchemaManifest schema = await fixture.Schemas.ReadAsync(paths.SchemaPath);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> holding = fixture.Handles.WithExclusiveAsync(paths, async entry =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            return true;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Task<bool> queued = operationKind switch
        {
            0 => fixture.Handles.WithExclusiveAsync(paths, entry => { entry.Open(false, schema); return true; }),
            1 => fixture.Handles.WithExclusiveAsync(paths, entry => { entry.Open(false, schema); return Task.FromResult(true); }),
            2 => fixture.Handles.WithSearcherAsync(paths, searcher => true, schema),
            3 => fixture.Handles.WithSearcherAsync(paths, (searcher, manifest) => true, entry => Task.FromResult(schema)),
            _ => throw new ArgumentOutOfRangeException(nameof(operationKind))
        };
        Task disposing = fixture.Handles.DisposeAsync().AsTask();
        release.SetResult();
        try
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await queued.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        }
        finally
        {
            release.TrySetResult();
            await holding.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            await disposing.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task DisposingOneTenantReleasesItsWriterWhileAnotherTenantRemainsOpen()
    {
        await using var first = new ProviderFixture();
        await using var second = new ProviderFixture();
        Assert.True(await first.Indexes.CreateAsync(first.Profile));
        Assert.True(await second.Indexes.CreateAsync(second.Profile));
        string firstPath = first.Paths.Resolve(first.Profile.IndexFullName).IndexDirectory;

        await first.Handles.DisposeAsync();

        using (var directory = new MMapDirectory(firstPath))
        using (var reopened = new IndexWriter(directory, new IndexWriterConfig()))
            reopened.Commit();

        Assert.True(await second.Documents.AddOrUpdateDocumentsAsync(second.Profile,
            [Article("remaining-tenant", "still owns its writer")]));
        Assert.Equal("remaining-tenant", Assert.Single(
            (await second.Search.SearchAsync(second.Profile, "writer", 0, 10)).ContentItemIds));
    }

    [Fact]
    public async Task LifecycleSupportsCreateIndexUpdateDeleteAllRebuildAndDelete()
    {
        await using var fixture = new ProviderFixture();
        Assert.False(await fixture.Indexes.ExistsAsync(fixture.Profile.IndexFullName));
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        Assert.False(await fixture.Indexes.CreateAsync(fixture.Profile));
        Assert.True(await fixture.Indexes.ExistsAsync(fixture.Profile.IndexFullName));

        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("article-1", "first orchard title") ]));
        Assert.Equal("article-1", Assert.Single((await fixture.Search.SearchAsync(fixture.Profile, "orchard", 0, 10)).ContentItemIds));

        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("article-1", "second revised title") ]));
        Assert.Empty((await fixture.Search.SearchAsync(fixture.Profile, "first", 0, 10)).ContentItemIds);
        Assert.Equal("article-1", Assert.Single((await fixture.Search.SearchAsync(fixture.Profile, "revised", 0, 10)).ContentItemIds));

        Assert.True(await fixture.Documents.DeleteDocumentsAsync(fixture.Profile, ["missing", "article-1"]));
        Assert.Empty((await fixture.Search.SearchAsync(fixture.Profile, "revised", 0, 10)).ContentItemIds);
        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("article-2", "orchard delete all") ]));
        Assert.True(await fixture.Documents.DeleteAllDocumentsAsync(fixture.Profile));
        Assert.True(await fixture.Indexes.ExistsAsync(fixture.Profile.IndexFullName));
        Assert.Empty((await fixture.Search.SearchAsync(fixture.Profile, "orchard", 0, 10)).ContentItemIds);

        await fixture.Documents.SetLastTaskIdAsync(fixture.Profile, 24);
        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("article-3", "rebuild meadow") ]));
        Assert.True(await fixture.Indexes.RebuildAsync(fixture.Profile));
        Assert.Equal(0, await fixture.Documents.GetLastTaskIdAsync(fixture.Profile));
        Assert.Empty((await fixture.Search.SearchAsync(fixture.Profile, "rebuild", 0, 10)).ContentItemIds);
        Assert.True(await fixture.Indexes.DeleteAsync(fixture.Profile));
        Assert.False(await fixture.Indexes.ExistsAsync(fixture.Profile.IndexFullName));
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
    }

    [Fact]
    public async Task BatchUpdateReplacesDocumentsAndKeepsTheLastDuplicateId()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
        [
            Article("batch-1", "original first"),
            Article("batch-2", "original second"),
        ]));

        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
        [
            Article("batch-1", "intermediate replacement"),
            Article("batch-2", "updated second"),
            Article("batch-1", "final replacement"),
        ]));

        Assert.Empty((await fixture.Search.SearchAsync(fixture.Profile, "original", 0, 10)).ContentItemIds);
        Assert.Empty((await fixture.Search.SearchAsync(fixture.Profile, "intermediate", 0, 10)).ContentItemIds);
        Assert.Equal("batch-1", Assert.Single((await fixture.Search.SearchAsync(fixture.Profile, "final replacement", 0, 10)).ContentItemIds));
        Assert.Equal("batch-2", Assert.Single((await fixture.Search.SearchAsync(fixture.Profile, "updated second", 0, 10)).ContentItemIds));
    }

    [Fact]
    public async Task R8ResetRewindsBeforeDeleteSoReplayRemainsSafeAfterFailure()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        string indexDirectory = fixture.Paths.Resolve(fixture.Profile.IndexFullName).IndexDirectory;
        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("reset-item", "reset replays this document") ]));
        await fixture.Documents.SetLastTaskIdAsync(fixture.Profile, 48);

        fixture.Failures.Point = LeanCorpusFailurePoint.AfterCursorPublish;
        Assert.False(await fixture.Documents.ResetAsync(fixture.Profile));

        Assert.Equal(0, await fixture.Documents.GetLastTaskIdAsync(fixture.Profile));
        Assert.True(await fixture.Indexes.ExistsAsync(fixture.Profile.IndexFullName));
        Assert.Equal(indexDirectory, fixture.Paths.Resolve(fixture.Profile.IndexFullName).IndexDirectory);
        Assert.Equal("reset-item", Assert.Single((await fixture.Search.SearchAsync(fixture.Profile, "replays this", 0, 10)).ContentItemIds));

        await fixture.RestartAsync();
        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("reset-item", "reset replays this document") ]));
        Assert.Equal("reset-item", Assert.Single((await fixture.Search.SearchAsync(fixture.Profile, "replays this", 0, 10)).ContentItemIds));

        await fixture.Documents.SetLastTaskIdAsync(fixture.Profile, 49);
        Assert.True(await fixture.Documents.ResetAsync(fixture.Profile));
        Assert.Equal(0, await fixture.Documents.GetLastTaskIdAsync(fixture.Profile));
        Assert.Empty((await fixture.Search.SearchAsync(fixture.Profile, "replays this", 0, 10)).ContentItemIds);
    }

    [Fact]
    public async Task IndexHandleIsSharedAcrossOrchardShellReplacement()
    {
        await using var fixture = new ProviderFixture();
        await using var replacementHandles = new LeanCorpusIndexHandleCache(fixture.Paths, lifetime: null, manualRootLifetimeContext: fixture.RootHostContext);
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));

        await fixture.Handles.DisposeAsync();

        var indexes = new LeanCorpusIndexManager(fixture.Paths, replacementHandles, fixture.Schemas, fixture.Failures,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LeanCorpusIndexManager>.Instance);
        var documents = new LeanCorpusDocumentIndexManager(fixture.Paths, replacementHandles, fixture.Schemas, fixture.Mapper,
            fixture.Failures, Microsoft.Extensions.Logging.Abstractions.NullLogger<LeanCorpusDocumentIndexManager>.Instance);
        var search = new LeanCorpusSearchService(fixture.Paths, replacementHandles, fixture.Schemas, new LeanCorpusSearchCompiler(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LeanCorpusSearchService>.Instance);

        Assert.True(await indexes.ExistsAsync(fixture.Profile.IndexFullName));
        Assert.True(await documents.AddOrUpdateDocumentsAsync(fixture.Profile, [Article("shell-reload", "reloaded shell writer") ]));
        Assert.Equal("shell-reload", Assert.Single((await search.SearchAsync(fixture.Profile, "shell writer", 0, 10)).ContentItemIds));
    }

    [Fact]
    public async Task ReplayingCommittedAndUncommittedBatchesIsIdempotent()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));

        fixture.Failures.Point = LeanCorpusFailurePoint.AfterDocumentWriteBeforeCommit;
        Assert.False(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("replay", "durable orchard") ]));
        Assert.Equal(0, await fixture.Documents.GetLastTaskIdAsync(fixture.Profile));
        Assert.Empty((await fixture.Search.SearchAsync(fixture.Profile, "durable", 0, 10)).ContentItemIds);

        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("replay", "durable orchard") ]));
        Assert.Equal("replay", Assert.Single((await fixture.Search.SearchAsync(fixture.Profile, "durable", 0, 10)).ContentItemIds));
        await fixture.Documents.SetLastTaskIdAsync(fixture.Profile, 18);

        fixture.Failures.Point = LeanCorpusFailurePoint.AfterCommitBeforeCursor;
        Assert.False(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("replay", "durable orchard updated") ]));
        Assert.Equal(18, await fixture.Documents.GetLastTaskIdAsync(fixture.Profile));
        await fixture.RestartAsync();
        Assert.Equal("replay", Assert.Single((await fixture.Search.SearchAsync(fixture.Profile, "updated", 0, 10)).ContentItemIds));

        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("replay", "durable orchard updated") ]));
        var result = await fixture.Search.SearchAsync(fixture.Profile, "durable", 0, 10);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("replay", Assert.Single(result.ContentItemIds));
    }

    [Fact]
    public async Task R1FailureBeforeWriteLeavesCursorAndDocumentsUnchangedThenReplaySucceeds()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        fixture.Failures.Point = LeanCorpusFailurePoint.BeforeDocumentWrite;

        Assert.False(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("r1", "replay after before-write failure")]));
        Assert.Equal(0, await fixture.Documents.GetLastTaskIdAsync(fixture.Profile));
        Assert.Empty((await fixture.Search.SearchAsync(fixture.Profile, "before-write", 0, 10)).ContentItemIds);

        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("r1", "replay after before-write failure")]));
        Assert.Equal("r1", Assert.Single((await fixture.Search.SearchAsync(fixture.Profile, "before-write", 0, 10)).ContentItemIds));
    }

    [Fact]
    public async Task R2UncommittedWriteCanBeReplayedAfterHandleRestart()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        fixture.Failures.Point = LeanCorpusFailurePoint.AfterDocumentWriteBeforeCommit;

        Assert.False(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("r2", "replay after uncommitted write")]));
        Assert.Equal(0, await fixture.Documents.GetLastTaskIdAsync(fixture.Profile));
        await fixture.RestartAsync();

        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("r2", "replay after uncommitted write")]));
        var results = await fixture.Search.SearchAsync(fixture.Profile, "uncommitted write", 0, 10);
        Assert.Equal(1, results.TotalCount);
        Assert.Equal("r2", Assert.Single(results.ContentItemIds));
    }

    [Fact]
    public async Task FailureAfterSchemaPublicationLeavesReplayableIndex()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        var article = Article("schema-replay", "new field value");
        article.Set("NewField", "first value", DocumentIndexOptions.None);

        fixture.Failures.Point = LeanCorpusFailurePoint.AfterSchemaPublishBeforeCommit;
        Assert.False(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile, [article]));
        Assert.Empty((await fixture.Search.SearchAsync(fixture.Profile, "first", 0, 10)).ContentItemIds);
        await fixture.RestartAsync();

        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile, [article]));
        Assert.Equal("schema-replay", Assert.Single((await fixture.Search.SearchAsync(fixture.Profile, "first", 0, 10)).ContentItemIds));
    }

    [Fact]
    public async Task DeleteFailureAfterCommitCanBeSafelyReplayed()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("delete-replay", "delete me") ]));

        fixture.Failures.Point = LeanCorpusFailurePoint.AfterDeleteCommitBeforeCursor;
        Assert.False(await fixture.Documents.DeleteDocumentsAsync(fixture.Profile, ["delete-replay"]));
        Assert.Empty((await fixture.Search.SearchAsync(fixture.Profile, "delete", 0, 10)).ContentItemIds);
        await fixture.RestartAsync();
        Assert.True(await fixture.Documents.DeleteDocumentsAsync(fixture.Profile, ["delete-replay"]));
    }

    [Fact]
    public async Task R6CursorPublicationFailureIsSafeOnlyAfterDurableDocumentCommit()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("r6", "durable before cursor publication")]));

        fixture.Failures.Point = LeanCorpusFailurePoint.AfterCursorPublish;
        await Assert.ThrowsAsync<IOException>(() => fixture.Documents.SetLastTaskIdAsync(fixture.Profile, 22));
        Assert.Equal(22, await fixture.Documents.GetLastTaskIdAsync(fixture.Profile));
        Assert.Equal("r6", Assert.Single((await fixture.Search.SearchAsync(fixture.Profile, "durable cursor", 0, 10)).ContentItemIds));
    }

    [Fact]
    public async Task CorruptCursorBlocksEveryMutationUntilRebuildResetsState()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("safe", "preserve existing document")]));
        await File.WriteAllTextAsync(fixture.Paths.Resolve(fixture.Profile.IndexFullName).StatePath, "{",
            TestContext.Current.CancellationToken);

        Assert.False(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile, [Article("unsafe", "must not be written")]));
        Assert.False(await fixture.Documents.DeleteDocumentsAsync(fixture.Profile, ["safe"]));
        Assert.False(await fixture.Documents.DeleteAllDocumentsAsync(fixture.Profile));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Documents.GetLastTaskIdAsync(fixture.Profile));
        Assert.Equal("safe", Assert.Single((await fixture.Search.SearchAsync(fixture.Profile, "preserve existing", 0, 10)).ContentItemIds));

        Assert.True(await fixture.Indexes.RebuildAsync(fixture.Profile));
        Assert.Equal(0, await fixture.Documents.GetLastTaskIdAsync(fixture.Profile));
        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile, [Article("unsafe", "repaired replay")]));
        Assert.Equal("unsafe", Assert.Single((await fixture.Search.SearchAsync(fixture.Profile, "repaired replay", 0, 10)).ContentItemIds));
    }

    [Fact]
    public async Task MissingCursorRestartsAtZeroAndAcceptsReplay()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        string cursorPath = fixture.Paths.Resolve(fixture.Profile.IndexFullName).StatePath;
        File.Delete(cursorPath);

        Assert.Equal(0, await fixture.Documents.GetLastTaskIdAsync(fixture.Profile));
        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("missing-cursor", "replay from initial task") ]));
        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("missing-cursor", "replay from initial task") ]));
        Assert.Equal(1, (await fixture.Search.SearchAsync(fixture.Profile, "initial task", 0, 10)).TotalCount);
    }

    [Fact]
    public async Task MissingSchemaAndExternallyRemovedIndexFailSafe()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("stored", "schema required for safe search")]));

        LeanCorpusIndexPaths paths = fixture.Paths.Resolve(fixture.Profile.IndexFullName);
        File.Delete(paths.SchemaPath);
        Assert.False(await fixture.Indexes.ExistsAsync(fixture.Profile.IndexFullName));
        Assert.False((await fixture.Search.SearchAsync(fixture.Profile, "schema required", 0, 10)).Success);
        Assert.False(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile, [Article("new", "cannot infer schema")]));

        Assert.True(await fixture.Indexes.RebuildAsync(fixture.Profile));
        await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile, [Article("stored", "external index removal")]);
        await fixture.RestartAsync();
        Directory.Delete(paths.IndexDirectory, recursive: true);
        Assert.False(await fixture.Indexes.ExistsAsync(fixture.Profile.IndexFullName));
        Assert.False((await fixture.Search.SearchAsync(fixture.Profile, "external index removal", 0, 10)).Success);
        Assert.False(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile, [Article("new", "must not recreate on write")]));
    }

    [Fact]
    public async Task CorruptPhysicalIndexCannotBeReportedOrOpenedAsUsable()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("corrupt", "index corruption must fail safe")]));
        string indexDirectory = fixture.Paths.Resolve(fixture.Profile.IndexFullName).IndexDirectory;
        await fixture.RestartAsync();

        string commitFile = Directory.EnumerateFiles(indexDirectory, "segments_*").Single();
        await File.WriteAllTextAsync(commitFile, "not a LeanCorpus commit", TestContext.Current.CancellationToken);

        Assert.False(await fixture.Indexes.ExistsAsync(fixture.Profile.IndexFullName));
        Assert.False((await fixture.Search.SearchAsync(fixture.Profile, "corruption", 0, 10)).Success);
        Assert.False(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile,
            [Article("new", "do not write into a corrupt index")]));
    }

    [Fact]
    public async Task RebuildReturnsFalseWhenOrchardDistributedLockIsUnavailable()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        var distributedLock = new RefusingDistributedLock();
        var indexes = new LeanCorpusIndexManager(fixture.Paths, fixture.Handles, fixture.Schemas, fixture.Failures,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LeanCorpusIndexManager>.Instance, distributedLock);

        Assert.False(await indexes.RebuildAsync(fixture.Profile));
        Assert.Equal($"LeanCorpusRebuild-{fixture.Profile.Id}", distributedLock.LastKey);
        Assert.True(await fixture.Indexes.ExistsAsync(fixture.Profile.IndexFullName));
    }

    [Fact]
    public async Task SearchUsesUnicodeAndStablePagination()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        var documents = Enumerable.Range(0, 300)
            .Select(index => Article($"paged-{index:D3}", "orchard Crème brûlée 東京 🚀 𐐷 e\u0301"))
            .Append(Article("c", "orchard Crème brûlée 東京 🚀 𐐷 e\u0301"))
            .Append(Article("a", "orchard Crème brûlée 東京 🚀 𐐷 e\u0301"))
            .Append(Article("b", "orchard Crème brûlée 東京 🚀 𐐷 e\u0301"))
            .ToArray();
        Assert.True(await fixture.Documents.AddOrUpdateDocumentsAsync(fixture.Profile, documents));

        var first = await fixture.Search.SearchAsync(fixture.Profile, "東京", 0, 10);
        Assert.Equal(303, first.TotalCount);
        Assert.Equal(new[] { "a", "b" }, first.ContentItemIds.Take(2));
        var punctuated = await fixture.Search.SearchAsync(fixture.Profile, "東京!!!", 0, 10);
        Assert.Equal(303, punctuated.TotalCount);
        var page = await fixture.Search.SearchAsync(fixture.Profile, "東京", 300, 3);
        Assert.Equal(303, page.TotalCount);
        Assert.Equal(new[] { "paged-297", "paged-298", "paged-299" }, page.ContentItemIds);
    }

    private static DocumentIndex Article(string id, string title)
    {
        var document = new DocumentIndex(id);
        document.Set("Title", title, DocumentIndexOptions.Store);
        return document;
    }

    private sealed class RefusingDistributedLock : IDistributedLock
    {
        public string? LastKey { get; private set; }

        public Task<ILocker> AcquireLockAsync(string key, TimeSpan? expiration = null)
            => throw new NotSupportedException();

        public Task<(ILocker, bool)> TryAcquireLockAsync(string key, TimeSpan timeout, TimeSpan? expiration = null)
        {
            LastKey = key;
            return Task.FromResult<(ILocker, bool)>((null!, false));
        }

        public Task<bool> IsLockAcquiredAsync(string key) => Task.FromResult(false);
    }
}

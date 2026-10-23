using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Core.Execution;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Server.Core.Tests;

[Trait("Area", "Server")]
public sealed class LocalIndexStoreTests
{
    [Fact]
    public async Task StartupRecoversPublishedRollbackAndRemovesAbandonedStaging()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-recovery-{Guid.NewGuid():N}");
        PhysicalIndexId id = PhysicalIndexId.New();
        string target = Path.Combine(root, id.Value);
        string previous = Path.Combine(root, $".previous-{id.Value}-recovery");
        try
        {
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "marker"), "old");
            Directory.Move(target, previous);
            Directory.CreateDirectory(Path.Combine(root, $".install-{id.Value}-staging"));
            Directory.CreateDirectory(Path.Combine(root, $".failed-{id.Value}-failed"));

            await using LocalIndexStore store = new(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            Assert.True(File.Exists(Path.Combine(target, "marker")));
            Assert.False(Directory.Exists(previous));
            Assert.False(Directory.Exists(Path.Combine(root, $".install-{id.Value}-staging")));
            Assert.False(Directory.Exists(Path.Combine(root, $".failed-{id.Value}-failed")));
            Assert.Equal(id, Assert.Single(store.List()));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PhysicalHandlesUseOpaqueIdsAndSupportSafeModeTransitions()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-physical-{Guid.NewGuid():N}");
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            PhysicalIndexId id = PhysicalIndexId.New();
            LocalIndexDescriptor descriptor = new(
                id,
                new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
                "schema-hash",
                new MutableIndexSettings(null, null, "content", null));

            await using LocalIndexHandle handle = await store.CreateAsync(descriptor, LocalIndexOpenMode.ReadOnly);
            Assert.True(store.Exists(id));
            Assert.Equal(id, Assert.Single(store.List()));
            Assert.Equal(LocalIndexOpenMode.ReadOnly, handle.Mode);
            Assert.True(await handle.CommitAsync() is CommitFailed);

            await handle.PromoteAsync();
            Assert.Equal(LocalIndexOpenMode.ReadWrite, handle.Mode);
            await handle.DemoteAsync();
            Assert.Equal(LocalIndexOpenMode.ReadOnly, handle.Mode);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task OpeningAfterCallerDisposedHandleWaitsAndReplacesTheCachedOwner()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-reopen-{Guid.NewGuid():N}");
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            LocalIndexDescriptor descriptor = new(PhysicalIndexId.New(),
                new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
                "reopen-schema", new MutableIndexSettings(null, null, "content", null));
            LocalIndexHandle oldHandle = await store.CreateAsync(descriptor);
            Guid oldRuntime = oldHandle.GetLifecycleDiagnostics().RuntimeId;
            await oldHandle.DisposeAsync();

            LocalIndexHandle reopened = await store.OpenAsync(descriptor, LocalIndexOpenMode.ReadWrite);
            Assert.NotSame(oldHandle, reopened);
            Assert.NotEqual(oldRuntime, reopened.GetLifecycleDiagnostics().RuntimeId);
            Assert.Equal(PhysicalIndexRuntimeState.Open, reopened.GetLifecycleDiagnostics().Physical.State);
            LocalIndexStoreDiagnostics storeDiagnostics = await store.GetLifecycleDiagnosticsAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, Assert.Single(storeDiagnostics.Entries).ReplacementGeneration);
            Assert.Equal(LocalIndexHandleLifecycleState.Active, Assert.Single(storeDiagnostics.Entries).State);

            await oldHandle.DisposeAsync();
            Assert.Equal(PhysicalIndexRuntimeState.Open, reopened.GetLifecycleDiagnostics().Physical.State);
            await reopened.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FailedCreateRemovesOnlyItsNewDirectoryAndAllowsSameIdRetry()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-create-rollback-{Guid.NewGuid():N}");
        PhysicalIndexId id = PhysicalIndexId.New();
        string path = Path.Combine(root, id.Value);
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            LocalIndexDescriptor invalid = new(id,
                new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
                "rollback-schema", new MutableIndexSettings(TimeSpan.Zero, null, "content", null));

            await Assert.ThrowsAnyAsync<Exception>(async () => await store.CreateAsync(invalid));
            Assert.False(Directory.Exists(path));
            LocalIndexStoreDiagnostics rollback = await store.GetLifecycleDiagnosticsAsync(TestContext.Current.CancellationToken);
            Assert.True(rollback.LastCreateRollbackSucceeded);
            Assert.Null(rollback.LastCreateRollbackFailure);

            LocalIndexDescriptor valid = invalid with { Settings = new MutableIndexSettings(null, null, "content", null) };
            LocalIndexHandle retried = await store.CreateAsync(valid);
            Assert.Equal(id, retried.Descriptor.Id);
            await retried.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CreateAgainstAnExistingPathPreservesItsContents()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-create-existing-{Guid.NewGuid():N}");
        PhysicalIndexId id = PhysicalIndexId.New();
        string path = Path.Combine(root, id.Value);
        string markerPath = Path.Combine(path, "existing-owner.marker");
        try
        {
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(markerPath, "existing owner data");
            await using LocalIndexStore store = new(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            LocalIndexDescriptor descriptor = new(id,
                new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
                "existing-path-schema", new MutableIndexSettings(null, null, "content", null));

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.CreateAsync(descriptor));

            Assert.Equal("existing owner data", await File.ReadAllTextAsync(markerPath));
            Assert.Empty((await store.GetLifecycleDiagnosticsAsync(TestContext.Current.CancellationToken)).Entries);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task IndependentWritableOwnerKeepsItsFilesWhenAnotherStoreCannotOpenThePath()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-independent-owner-{Guid.NewGuid():N}");
        PhysicalIndexId id = PhysicalIndexId.New();
        string path = Path.Combine(root, id.Value);
        string markerPath = Path.Combine(path, "first-owner.marker");
        try
        {
            await using LocalIndexStore firstStore = new(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            await using LocalIndexStore secondStore = new(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            LocalIndexDescriptor descriptor = new(id,
                new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
                "independent-owner-schema", new MutableIndexSettings(null, null, "content", null));
            LocalIndexHandle first = await firstStore.CreateAsync(descriptor);
            await File.WriteAllTextAsync(markerPath, "first owner data");

            await Assert.ThrowsAsync<WriteLockException>(async () =>
                await secondStore.OpenAsync(descriptor, LocalIndexOpenMode.ReadWrite, TestContext.Current.CancellationToken));

            Assert.Equal("first owner data", await File.ReadAllTextAsync(markerPath));
            Assert.Equal(PhysicalIndexRuntimeState.Open, first.GetLifecycleDiagnostics().Physical.State);
            Assert.Throws<IOException>(() => File.Open(Path.Combine(path, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FailedCreateAfterPhysicalOpenClosesOwnerRemovesDirectoryAndAllowsRetry()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-create-open-rollback-{Guid.NewGuid():N}");
        PhysicalIndexId id = PhysicalIndexId.New();
        string path = Path.Combine(root, id.Value);
        IOException createFailure = new("Injected failure before physical owner publication.");
        LocalIndexHandle? failedHandle = null;
        int failNextPublication = 1;
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1),
                observer: null, installOperations: null, deleteDirectory: null,
                beforeHandlePublished: handle =>
                {
                    failedHandle = handle;
                    if (Interlocked.Exchange(ref failNextPublication, 0) == 1)
                        throw createFailure;
                });
            LocalIndexDescriptor descriptor = new(id,
                new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
                "post-open-rollback-schema", new MutableIndexSettings(null, null, "content", null));

            IOException failure = await Assert.ThrowsAsync<IOException>(async () => await store.CreateAsync(descriptor));

            Assert.Same(createFailure, failure);
            Assert.NotNull(failedHandle);
            Assert.Equal(LocalIndexHandleLifecycleState.Closed, failedHandle.LifecycleState);
            Assert.Equal(PhysicalIndexRuntimeState.Closed, failedHandle.GetLifecycleDiagnostics().Physical.State);
            Assert.False(Directory.Exists(path));
            LocalIndexStoreDiagnostics diagnostics = await store.GetLifecycleDiagnosticsAsync(TestContext.Current.CancellationToken);
            Assert.Empty(diagnostics.Entries);
            Assert.True(diagnostics.LastCreateRollbackSucceeded);
            Assert.Null(diagnostics.LastCreateRollbackFailure);

            LocalIndexHandle retry = await store.CreateAsync(descriptor);
            Assert.Equal(id, retry.Descriptor.Id);
            await retry.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FailedCreateCleanupFailurePreservesBothErrorsAndBlocksReopen()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-create-cleanup-failure-{Guid.NewGuid():N}");
        PhysicalIndexId id = PhysicalIndexId.New();
        string path = Path.Combine(root, id.Value);
        IOException createFailure = new("Injected physical create failure.");
        IOException cleanupFailure = new("Injected create rollback failure.");
        LocalIndexHandle? failedHandle = null;
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1),
                observer: null, installOperations: null,
                deleteDirectory: (_, _) => throw cleanupFailure,
                beforeHandlePublished: handle =>
                {
                    failedHandle = handle;
                    throw createFailure;
                });
            LocalIndexDescriptor descriptor = new(id,
                new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
                "rollback-cleanup-schema", new MutableIndexSettings(null, null, "content", null));

            AggregateException failure = await Assert.ThrowsAsync<AggregateException>(async () => await store.CreateAsync(descriptor));

            Assert.Same(createFailure, failure.InnerExceptions[0]);
            Assert.Same(cleanupFailure, failure.InnerExceptions[1]);
            Assert.NotNull(failedHandle);
            Assert.Equal(LocalIndexHandleLifecycleState.Closed, failedHandle.LifecycleState);
            Assert.Equal(PhysicalIndexRuntimeState.Closed, failedHandle.GetLifecycleDiagnostics().Physical.State);
            Assert.True(Directory.Exists(path));
            LocalIndexStoreDiagnostics diagnostics = await store.GetLifecycleDiagnosticsAsync(TestContext.Current.CancellationToken);
            Assert.Empty(diagnostics.Entries);
            Assert.False(diagnostics.LastCreateRollbackSucceeded);
            Assert.Same(failure, diagnostics.LastCreateRollbackFailure);
            Assert.Contains(id.Value, diagnostics.RetainedFailedCreateIds);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.OpenAsync(descriptor, LocalIndexOpenMode.ReadWrite, TestContext.Current.CancellationToken));
            Assert.Empty((await store.GetLifecycleDiagnosticsAsync(TestContext.Current.CancellationToken)).Entries);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ReadOnlyCopyCanInstallPinnedCommitAndThenSearch()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-install-{Guid.NewGuid():N}");
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            IndexSchema schema = new([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>());
            MutableIndexSettings settings = new(null, null, "content", null);
            LocalIndexDescriptor sourceDescriptor = new(PhysicalIndexId.New(), schema, "same-schema", settings, new IndexTopologySettings(1, 0));
            LocalIndexDescriptor targetDescriptor = sourceDescriptor with { Id = PhysicalIndexId.New() };
            await using LocalIndexHandle source = await store.CreateAsync(sourceDescriptor, LocalIndexOpenMode.ReadWrite);
            await using LocalIndexHandle target = await store.CreateAsync(targetDescriptor, LocalIndexOpenMode.ReadOnly);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root });
            using JsonDocument document = JsonDocument.Parse("{\"content\":\"replica source\"}");
            await executor.WriteAsync(new OperationContext("write", OperationKind.WriteDocuments, CallerIdentity.Anonymous, DateTimeOffset.UtcNow), source,
                new BulkDocumentsRequest("source", [new BulkDocumentOperation(DocumentOperationKind.Index, "one", document.RootElement.Clone())], Refresh: true));

            await using CommitSnapshotLease lease = await source.AcquireCommitSnapshotAsync();
            CommitInstallResult installed = await target.InstallCommitAsync(lease);
            Assert.True(installed is CommitInstalled);
            Assert.True(await target.InstallCommitAsync(lease) is CommitAlreadyPresent);
            SearchResponse search = await executor.SearchAsync(new OperationContext("search", OperationKind.Search, CallerIdentity.Anonymous, DateTimeOffset.UtcNow), target,
                new SearchRequest(new TermQueryDefinition("content", "replica")));
            Assert.Single(search.Hits);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SnapshotLeaseKeepsItsManifestFilesReadableAcrossNewCommit()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-snapshot-{Guid.NewGuid():N}");
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            LocalIndexDescriptor descriptor = new(PhysicalIndexId.New(),
                new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
                "snapshot-schema", new MutableIndexSettings(null, null, "content", null), new IndexTopologySettings(1, 0));
            await using LocalIndexHandle handle = await store.CreateAsync(descriptor);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root });
            using JsonDocument first = JsonDocument.Parse("{\"content\":\"first snapshot\"}");
            await executor.WriteAsync(new OperationContext("snapshot-1", OperationKind.WriteDocuments, CallerIdentity.Anonymous, DateTimeOffset.UtcNow), handle,
                new BulkDocumentsRequest("index", [new BulkDocumentOperation(DocumentOperationKind.Index, "one", first.RootElement.Clone())], Refresh: true));
            await using CommitSnapshotLease lease = await handle.AcquireCommitSnapshotAsync();
            string fileName = lease.Manifest.Files.First().FileName;
            using (Stream before = lease.OpenRead(fileName))
                Assert.True(before.Length > 0);

            using JsonDocument second = JsonDocument.Parse("{\"content\":\"second snapshot\"}");
            await executor.WriteAsync(new OperationContext("snapshot-2", OperationKind.WriteDocuments, CallerIdentity.Anonymous, DateTimeOffset.UtcNow), handle,
                new BulkDocumentsRequest("index", [new BulkDocumentOperation(DocumentOperationKind.Index, "two", second.RootElement.Clone())], Refresh: true));
            using Stream after = lease.OpenRead(fileName);
            Assert.True(after.Length > 0);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}

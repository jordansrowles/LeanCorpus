using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Core.Execution;
using Rowles.LeanCorpus.Server.Core.Configuration;

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

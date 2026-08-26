using System.Text.Json;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Execution;

namespace Rowles.LeanCorpus.Server.Core.Tests;

[Trait("Area", "Server")]
public sealed class TwoCopyAcceptanceTests
{
    [Fact]
    public async Task TenThousandDocumentCopySupportsEquivalentLexicalAndFilteredQueries()
    {
        string root = NewRoot("two-copy-10k");
        try
        {
            await using LocalIndexStore store = NewStore(root);
            LocalIndexDescriptor sourceDescriptor = Descriptor(PhysicalIndexId.New());
            LocalIndexDescriptor targetDescriptor = sourceDescriptor with { Id = PhysicalIndexId.New() };
            await using LocalIndexHandle source = await store.CreateAsync(sourceDescriptor, LocalIndexOpenMode.ReadWrite);
            await using LocalIndexHandle target = await store.CreateAsync(targetDescriptor, LocalIndexOpenMode.ReadOnly);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root, MaximumSearchResults = 100 });

            LocalCommitReceipt receipt = await WriteBatchAsync(executor, source, 0, 10_000);
            await using CommitSnapshotLease snapshot = await source.AcquireCommitSnapshotAsync();
            Assert.Equal(receipt.CommitGeneration, snapshot.CommitGeneration);
            Assert.True(await target.InstallCommitAsync(snapshot) is CommitInstalled);

            Assert.Equal(snapshot.CommitGeneration, target.Health.VisibleGeneration);
            using SearcherLease sourceView = source.Runtime.Searchers.AcquireLease();
            using SearcherLease targetView = target.Runtime.Searchers.AcquireLease();
            Assert.Equal(sourceView.ContentToken, targetView.ContentToken);
            Assert.Equal(sourceView.CommitGeneration, targetView.CommitGeneration);

            await AssertEquivalentAsync(executor, source, target, new TermQueryDefinition("content", "searchable"));
            await AssertEquivalentAsync(executor, source, target, new TermQueryDefinition("group", "even"));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task PinnedSnapshotSurvivesMergePressureAndCanPopulateAnotherCopy()
    {
        string root = NewRoot("two-copy-pinned");
        try
        {
            await using LocalIndexStore store = NewStore(root);
            LocalIndexDescriptor sourceDescriptor = Descriptor(PhysicalIndexId.New());
            await using LocalIndexHandle source = await store.CreateAsync(sourceDescriptor);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root, MaximumSearchResults = 100 });
            await WriteBatchAsync(executor, source, 0, 100);

            await using CommitSnapshotLease pinned = await source.AcquireCommitSnapshotAsync();
            foreach (int batch in Enumerable.Range(1, 5))
                await WriteBatchAsync(executor, source, batch * 100, 100);

            source.Runtime.Writer.ForceMerge(1);
            foreach (IndexBackupFileEntry entry in pinned.Manifest.Files.Where(static entry => entry.PresentInBackup))
            {
                using Stream stream = pinned.OpenRead(entry.FileName);
                Assert.Equal(entry.Length, stream.Length);
            }

            LocalIndexDescriptor targetDescriptor = sourceDescriptor with { Id = PhysicalIndexId.New() };
            await using LocalIndexHandle target = await store.CreateAsync(targetDescriptor, LocalIndexOpenMode.ReadOnly);
            Assert.True(await target.InstallCommitAsync(pinned) is CommitInstalled);
            SearchResponse search = await SearchAsync(executor, target, new TermQueryDefinition("content", "searchable"));
            Assert.Equal(100, search.TotalHits);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task CorruptOrIncompleteTransferPreservesTheOldTargetGeneration()
    {
        string root = NewRoot("two-copy-corrupt");
        CorruptingOperations operations = new();
        try
        {
            await using LocalIndexStore store = NewStore(root, operations);
            LocalIndexDescriptor sourceDescriptor = Descriptor(PhysicalIndexId.New());
            LocalIndexDescriptor targetDescriptor = sourceDescriptor with { Id = PhysicalIndexId.New() };
            await using LocalIndexHandle source = await store.CreateAsync(sourceDescriptor);
            await using LocalIndexHandle target = await store.CreateAsync(targetDescriptor, LocalIndexOpenMode.ReadOnly);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root, MaximumSearchResults = 100 });

            await WriteBatchAsync(executor, source, 0, 2);
            await using CommitSnapshotLease first = await source.AcquireCommitSnapshotAsync();
            Assert.True(await target.InstallCommitAsync(first) is CommitInstalled);

            await WriteBatchAsync(executor, source, 2, 2);
            await using CommitSnapshotLease second = await source.AcquireCommitSnapshotAsync();
            foreach (CorruptionKind corruption in new[] { CorruptionKind.MissingFile, CorruptionKind.WrongLength, CorruptionKind.WrongChecksum })
            {
                operations.Corruption = corruption;
                Assert.True(await target.InstallCommitAsync(second) is CommitRejected);
                Assert.Equal(first.CommitGeneration, target.Health.VisibleGeneration);
                Assert.Single((await SearchAsync(executor, target, new TermQueryDefinition("_id", "doc-0"))).Hits);
                AssertNoInstallArtifacts(root);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task CancellationAndPublicationFailurePreserveTheOldTargetAndStaleInstallIsRejected()
    {
        string root = NewRoot("two-copy-failures");
        FailingOperations operations = new();
        using CancellationTokenSource cancellation = new();
        try
        {
            await using LocalIndexStore store = NewStore(root, operations);
            LocalIndexDescriptor sourceDescriptor = Descriptor(PhysicalIndexId.New());
            LocalIndexDescriptor targetDescriptor = sourceDescriptor with { Id = PhysicalIndexId.New() };
            await using LocalIndexHandle source = await store.CreateAsync(sourceDescriptor);
            await using LocalIndexHandle target = await store.CreateAsync(targetDescriptor, LocalIndexOpenMode.ReadOnly);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root, MaximumSearchResults = 100 });

            await WriteBatchAsync(executor, source, 0, 2);
            await using CommitSnapshotLease first = await source.AcquireCommitSnapshotAsync();
            Assert.True(await target.InstallCommitAsync(first) is CommitInstalled);

            await WriteBatchAsync(executor, source, 2, 2);
            await using CommitSnapshotLease second = await source.AcquireCommitSnapshotAsync();

            operations.CancelBeforeMaterialisation = true;
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => target.InstallCommitAsync(second, cancellation.Token).AsTask());
            Assert.Equal(first.CommitGeneration, target.Health.VisibleGeneration);
            AssertNoInstallArtifacts(root);

            operations.CancelBeforeMaterialisation = false;
            operations.FailPublication = true;
            Assert.True(await target.InstallCommitAsync(second) is CommitRejected);
            Assert.Equal(first.CommitGeneration, target.Health.VisibleGeneration);
            AssertNoInstallArtifacts(root);

            operations.FailPublication = false;
            Assert.True(await target.InstallCommitAsync(second) is CommitInstalled);
            Assert.Equal(second.CommitGeneration, target.Health.VisibleGeneration);
            Assert.True(await target.InstallCommitAsync(first) is CommitRejected);
            Assert.Equal(second.CommitGeneration, target.Health.VisibleGeneration);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ReadOnlyCopyCanPromoteWriteAndConvergeBackIntoTheOriginalCopy()
    {
        string root = NewRoot("two-copy-reverse");
        try
        {
            await using LocalIndexStore store = NewStore(root);
            LocalIndexDescriptor sourceDescriptor = Descriptor(PhysicalIndexId.New());
            LocalIndexDescriptor targetDescriptor = sourceDescriptor with { Id = PhysicalIndexId.New() };
            await using LocalIndexHandle source = await store.CreateAsync(sourceDescriptor);
            await using LocalIndexHandle target = await store.CreateAsync(targetDescriptor, LocalIndexOpenMode.ReadOnly);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root, MaximumSearchResults = 100 });

            await WriteBatchAsync(executor, source, 0, 2);
            await using CommitSnapshotLease first = await source.AcquireCommitSnapshotAsync();
            Assert.True(await target.InstallCommitAsync(first) is CommitInstalled);

            await target.PromoteAsync();
            Assert.Equal(LocalIndexOpenMode.ReadWrite, target.Mode);
            await WriteBatchAsync(executor, target, 2, 1);
            await using CommitSnapshotLease promoted = await target.AcquireCommitSnapshotAsync();

            await source.DemoteAsync();
            Assert.Equal(LocalIndexOpenMode.ReadOnly, source.Mode);
            Assert.True(await source.InstallCommitAsync(promoted) is CommitInstalled);
            Assert.Equal(promoted.CommitGeneration, source.Health.VisibleGeneration);
            Assert.Single((await SearchAsync(executor, source, new TermQueryDefinition("_id", "doc-2"))).Hits);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static LocalIndexStore NewStore(string root, ICommitInstallOperations? operations = null) =>
        operations is null
            ? new LocalIndexStore(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1))
            : new LocalIndexStore(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), observer: null, installOperations: operations);

    private static LocalIndexDescriptor Descriptor(PhysicalIndexId id) => new(
        id,
        new IndexSchema(
        [
            new IndexFieldDefinition("content", IndexFieldType.Text, true, true),
            new IndexFieldDefinition("group", IndexFieldType.Keyword, true, true),
            new IndexFieldDefinition("rank", IndexFieldType.Int64, true, true),
            new IndexFieldDefinition("ratio", IndexFieldType.Double, true, true)
        ], new Dictionary<string, AnalysisDefinition>()),
        "two-copy-schema",
        new MutableIndexSettings(null, null, "content", null),
        new IndexTopologySettings(1, 0));

    private static async Task<LocalCommitReceipt> WriteBatchAsync(LocalIndexExecutor executor, LocalIndexHandle handle, int start, int count)
    {
        List<BulkDocumentOperation> operations = new(count);
        for (int i = start; i < start + count; i++)
        {
            JsonElement document = JsonSerializer.SerializeToElement(new
            {
                content = $"searchable doc-{i}",
                group = i % 2 == 0 ? "even" : "odd",
                rank = (long)i,
                ratio = i + 0.5
            });
            operations.Add(new BulkDocumentOperation(DocumentOperationKind.Index, $"doc-{i}", document));
        }

        LocalWriteResult result = await executor.WriteAsync(
            new OperationContext("two-copy-write", OperationKind.WriteDocuments, CallerIdentity.Anonymous, DateTimeOffset.UtcNow),
            handle,
            new BulkDocumentsRequest("two-copy", operations, Refresh: true, Durability: RequestedWriteDurability.LocalFsync));
        Assert.True(result.Committed);
        return result.Receipt!;
    }

    private static async Task AssertEquivalentAsync(
        LocalIndexExecutor executor,
        LocalIndexHandle source,
        LocalIndexHandle target,
        QueryDefinition query)
    {
        SearchResponse sourceResponse = await SearchAsync(executor, source, query);
        SearchResponse targetResponse = await SearchAsync(executor, target, query);
        Assert.Equal(sourceResponse.TotalHits, targetResponse.TotalHits);
        Assert.Equal(
            sourceResponse.Hits.Select(static hit => hit.DocumentId),
            targetResponse.Hits.Select(static hit => hit.DocumentId));
    }

    private static Task<SearchResponse> SearchAsync(LocalIndexExecutor executor, LocalIndexHandle handle, QueryDefinition query) =>
        executor.SearchAsync(
            new OperationContext("two-copy-search", OperationKind.Search, CallerIdentity.Anonymous, DateTimeOffset.UtcNow),
            handle,
            new SearchRequest(query, Size: 20, IncludeDocuments: false)).AsTask();

    private static string NewRoot(string name) => Path.Combine(Path.GetTempPath(), $"lean-corpus-{name}-{Guid.NewGuid():N}");

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static void AssertNoInstallArtifacts(string root)
    {
        Assert.DoesNotContain(
            Directory.EnumerateDirectories(root),
            static path => Path.GetFileName(path).StartsWith(".install-", StringComparison.Ordinal));
    }

    private enum CorruptionKind
    {
        MissingFile,
        WrongLength,
        WrongChecksum
    }

    private sealed class CorruptingOperations : ICommitInstallOperations
    {
        internal CorruptionKind? Corruption { get; set; }

        public void Materialise(CommitSnapshotLease lease, string backupDirectoryPath, string materialisedDirectoryPath, CancellationToken cancellationToken)
        {
            if (Corruption is null)
            {
                DefaultCommitInstallOperations.Instance.Materialise(lease, backupDirectoryPath, materialisedDirectoryPath, cancellationToken);
                return;
            }

            ICommitSnapshotSource source = Assert.IsAssignableFrom<ICommitSnapshotSource>(lease);
            source.CreateBackup(backupDirectoryPath, cancellationToken);
            IndexBackupManifest manifest = IndexBackup.ReadManifest(backupDirectoryPath);
            IndexBackupFileEntry entry = manifest.Files.First(file => file.PresentInBackup && file.Length > 0);
            string path = Path.Combine(backupDirectoryPath, entry.FileName);
            switch (Corruption)
            {
                case CorruptionKind.MissingFile:
                    File.Delete(path);
                    break;
                case CorruptionKind.WrongLength:
                    using (FileStream stream = new(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                        stream.WriteByte(0);
                    break;
                case CorruptionKind.WrongChecksum:
                    byte[] bytes = File.ReadAllBytes(path);
                    bytes[0] ^= 0xFF;
                    File.WriteAllBytes(path, bytes);
                    break;
            }

            IndexBackup.Restore(
                backupDirectoryPath,
                materialisedDirectoryPath,
                new IndexRestoreOptions { OverwriteTargetDirectory = false, ValidateAfterRestore = true },
                cancellationToken);
        }

        public void MoveDirectory(string sourcePath, string destinationPath) => Directory.Move(sourcePath, destinationPath);
    }

    private sealed class FailingOperations : ICommitInstallOperations
    {
        internal bool CancelBeforeMaterialisation { get; set; }
        internal bool FailPublication { get; set; }

        public void Materialise(CommitSnapshotLease lease, string backupDirectoryPath, string materialisedDirectoryPath, CancellationToken cancellationToken)
        {
            if (CancelBeforeMaterialisation)
                cancellationToken.ThrowIfCancellationRequested();
            DefaultCommitInstallOperations.Instance.Materialise(lease, backupDirectoryPath, materialisedDirectoryPath, cancellationToken);
        }

        public void MoveDirectory(string sourcePath, string destinationPath)
        {
            if (FailPublication)
                throw new IOException("injected publication failure");
            Directory.Move(sourcePath, destinationPath);
        }
    }
}

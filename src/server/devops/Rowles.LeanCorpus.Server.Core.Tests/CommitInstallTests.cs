using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Execution;

namespace Rowles.LeanCorpus.Server.Core.Tests;

[Trait("Area", "Server")]
public sealed class CommitInstallTests
{
    [Fact]
    public async Task InstallOlderGenerationIsRejectedWithoutMutation()
    {
        string root = NewRoot("stale-install");
        try
        {
            await using LocalIndexStore store = NewStore(root);
            await using LocalIndexHandle source = await store.CreateAsync(Descriptor(PhysicalIndexId.New()));
            await using LocalIndexHandle target = await store.CreateAsync(Descriptor(PhysicalIndexId.New()), LocalIndexOpenMode.ReadOnly);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root });

            await WriteAsync(executor, source, "one", "first generation");
            CommitSnapshotLease older = await source.AcquireCommitSnapshotAsync();
            try
            {
                await WriteAsync(executor, source, "two", "second generation");
                await using CommitSnapshotLease newer = await source.AcquireCommitSnapshotAsync();
                Assert.True(await target.InstallCommitAsync(newer) is CommitInstalled);

                CommitInstallResult stale = await target.InstallCommitAsync(older);

                Assert.True(stale is CommitRejected);
                Assert.Equal(newer.CommitGeneration, target.Health.VisibleGeneration);
                SearchResponse search = await SearchAsync(executor, target, "second");
                Assert.Single(search.Hits);
                Assert.Equal("two", search.Hits[0].DocumentId);
            }
            finally
            {
                await older.DisposeAsync();
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task CancelledBeforeMaterialisationLeavesOldGenerationReadable()
    {
        string root = NewRoot("cancel-before-install");
        using CancellationTokenSource cancellation = new();
        CancellationMaterialiser materialiser = new(cancellation);
        try
        {
            await using LocalIndexStore store = NewStore(root, materialiser);
            await using LocalIndexHandle source = await store.CreateAsync(Descriptor(PhysicalIndexId.New()));
            await using LocalIndexHandle target = await store.CreateAsync(Descriptor(PhysicalIndexId.New()), LocalIndexOpenMode.ReadOnly);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root });

            await WriteAsync(executor, source, "one", "old visible generation");
            await using CommitSnapshotLease first = await source.AcquireCommitSnapshotAsync();
            Assert.True(await target.InstallCommitAsync(first) is CommitInstalled);

            await WriteAsync(executor, source, "two", "new candidate generation");
            await using CommitSnapshotLease second = await source.AcquireCommitSnapshotAsync();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => target.InstallCommitAsync(second, cancellation.Token).AsTask());

            Assert.True(target.Health.IsUsable);
            Assert.Equal(first.CommitGeneration, target.Health.VisibleGeneration);
            Assert.Single((await SearchAsync(executor, target, "old")).Hits);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task CancelledDuringMaterialisationLeavesOldGenerationReadable()
    {
        string root = NewRoot("cancel-during-install");
        using CancellationTokenSource cancellation = new();
        CancellationMaterialiser materialiser = new(cancellation) { CancelDuringMaterialisation = true };
        try
        {
            await using LocalIndexStore store = NewStore(root, materialiser);
            await using LocalIndexHandle source = await store.CreateAsync(Descriptor(PhysicalIndexId.New()));
            await using LocalIndexHandle target = await store.CreateAsync(Descriptor(PhysicalIndexId.New()), LocalIndexOpenMode.ReadOnly);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root });

            await WriteAsync(executor, source, "one", "old visible generation");
            await using CommitSnapshotLease first = await source.AcquireCommitSnapshotAsync();
            materialiser.CancelDuringMaterialisation = false;
            Assert.True(await target.InstallCommitAsync(first) is CommitInstalled);

            await WriteAsync(executor, source, "two", "new candidate generation");
            await using CommitSnapshotLease second = await source.AcquireCommitSnapshotAsync();
            materialiser.CancelDuringMaterialisation = true;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => target.InstallCommitAsync(second, cancellation.Token).AsTask());

            Assert.True(target.Health.IsUsable);
            Assert.Equal(first.CommitGeneration, target.Health.VisibleGeneration);
            Assert.Single((await SearchAsync(executor, target, "old")).Hits);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task FailureBeforePublicationLeavesOldGenerationReadable()
    {
        string root = NewRoot("failed-install");
        ThrowingMaterialiser materialiser = new();
        try
        {
            await using LocalIndexStore store = NewStore(root, materialiser);
            await using LocalIndexHandle source = await store.CreateAsync(Descriptor(PhysicalIndexId.New()));
            await using LocalIndexHandle target = await store.CreateAsync(Descriptor(PhysicalIndexId.New()), LocalIndexOpenMode.ReadOnly);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root });

            await WriteAsync(executor, source, "one", "old visible generation");
            await using CommitSnapshotLease first = await source.AcquireCommitSnapshotAsync();
            materialiser.Enabled = false;
            Assert.True(await target.InstallCommitAsync(first) is CommitInstalled);

            await WriteAsync(executor, source, "two", "new candidate generation");
            await using CommitSnapshotLease second = await source.AcquireCommitSnapshotAsync();
            materialiser.Enabled = true;

            CommitInstallResult result = await target.InstallCommitAsync(second);

            Assert.True(result is CommitRejected);
            Assert.True(target.Health.IsUsable);
            Assert.True(target.Health.IsDegraded);
            Assert.Contains("material", target.Health.LastInstallError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(first.CommitGeneration, target.Health.VisibleGeneration);
            Assert.Single((await SearchAsync(executor, target, "old")).Hits);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task PublicationFailureRollsBackToThePreviousReadableGeneration()
    {
        string root = NewRoot("publication-rollback");
        FailingMoveOperations operations = new(failMoveNumber: 2);
        try
        {
            await using LocalIndexStore store = NewStore(root, operations);
            await using LocalIndexHandle source = await store.CreateAsync(Descriptor(PhysicalIndexId.New()));
            await using LocalIndexHandle target = await store.CreateAsync(Descriptor(PhysicalIndexId.New()), LocalIndexOpenMode.ReadOnly);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root });

            await WriteAsync(executor, source, "one", "old visible generation");
            await using CommitSnapshotLease first = await source.AcquireCommitSnapshotAsync();
            Assert.True(await target.InstallCommitAsync(first) is CommitInstalled);

            await WriteAsync(executor, source, "two", "new candidate generation");
            await using CommitSnapshotLease second = await source.AcquireCommitSnapshotAsync();
            operations.Enabled = true;

            CommitInstallResult result = await target.InstallCommitAsync(second);

            Assert.True(result is CommitRejected);
            Assert.True(target.Health.IsUsable);
            Assert.Equal(first.CommitGeneration, target.Health.VisibleGeneration);
            Assert.Single((await SearchAsync(executor, target, "old")).Hits);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task RollbackFailureMarksTheHandleUnusable()
    {
        string root = NewRoot("rollback-failure");
        FailingMoveOperations operations = new(failMoveNumber: 2, failEveryMoveFrom: 3);
        try
        {
            await using LocalIndexStore store = NewStore(root, operations);
            await using LocalIndexHandle source = await store.CreateAsync(Descriptor(PhysicalIndexId.New()));
            await using LocalIndexHandle target = await store.CreateAsync(Descriptor(PhysicalIndexId.New()), LocalIndexOpenMode.ReadOnly);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root });

            await WriteAsync(executor, source, "one", "old visible generation");
            await using CommitSnapshotLease first = await source.AcquireCommitSnapshotAsync();
            Assert.True(await target.InstallCommitAsync(first) is CommitInstalled);

            await WriteAsync(executor, source, "two", "new candidate generation");
            await using CommitSnapshotLease second = await source.AcquireCommitSnapshotAsync();
            operations.Enabled = true;

            CommitInstallResult result = await target.InstallCommitAsync(second);

            Assert.True(result is CommitRejected rejected && rejected.Message.Contains("unusable", StringComparison.OrdinalIgnoreCase));
            Assert.False(target.Health.IsUsable);
            Assert.True(target.Health.IsDegraded);
            Assert.Contains("Rollback failed", target.Health.LastInstallError, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task WritableTargetRejectsInstall()
    {
        string root = NewRoot("writable-target");
        try
        {
            await using LocalIndexStore store = NewStore(root);
            await using LocalIndexHandle source = await store.CreateAsync(Descriptor(PhysicalIndexId.New()));
            await using LocalIndexHandle target = await store.CreateAsync(Descriptor(PhysicalIndexId.New()), LocalIndexOpenMode.ReadWrite);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root });
            await WriteAsync(executor, source, "one", "source");
            await using CommitSnapshotLease lease = await source.AcquireCommitSnapshotAsync();

            Assert.True(await target.InstallCommitAsync(lease) is CommitRejected);
            Assert.Equal(LocalIndexOpenMode.ReadWrite, target.Mode);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task WrongSchemaRejectsInstall()
    {
        string root = NewRoot("wrong-schema");
        try
        {
            await using LocalIndexStore store = NewStore(root);
            await using LocalIndexHandle source = await store.CreateAsync(Descriptor(PhysicalIndexId.New(), "content"));
            await using LocalIndexHandle target = await store.CreateAsync(Descriptor(PhysicalIndexId.New(), "title"), LocalIndexOpenMode.ReadOnly);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root });
            await WriteAsync(executor, source, "one", "source");
            await using CommitSnapshotLease lease = await source.AcquireCommitSnapshotAsync();

            CommitInstallResult result = await target.InstallCommitAsync(lease);

            Assert.True(result is CommitRejected rejected && rejected.Message.Contains("schema", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(0, target.Health.VisibleGeneration);
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

    private static LocalIndexDescriptor Descriptor(PhysicalIndexId id, string field = "content") => new(
        id,
        new IndexSchema([new IndexFieldDefinition(field, IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
        $"schema-{field}",
        new MutableIndexSettings(null, null, field, null),
        new IndexTopologySettings(1, 0));

    private static async Task WriteAsync(LocalIndexExecutor executor, LocalIndexHandle handle, string id, string content)
    {
        using JsonDocument document = JsonDocument.Parse($"{{\"content\":{JsonSerializer.Serialize(content)}}}");
        LocalWriteResult result = await executor.WriteAsync(
            new OperationContext("install-test", OperationKind.WriteDocuments, CallerIdentity.Anonymous, DateTimeOffset.UtcNow),
            handle,
            new BulkDocumentsRequest("index", [new BulkDocumentOperation(DocumentOperationKind.Index, id, document.RootElement.Clone())], Refresh: true));
        Assert.True(result.Committed);
    }

    private static Task<SearchResponse> SearchAsync(LocalIndexExecutor executor, LocalIndexHandle handle, string term) =>
        executor.SearchAsync(
            new OperationContext("install-search", OperationKind.Search, CallerIdentity.Anonymous, DateTimeOffset.UtcNow),
            handle,
            new SearchRequest(new TermQueryDefinition("content", term))).AsTask();

    private static string NewRoot(string name) => Path.Combine(Path.GetTempPath(), $"lean-corpus-{name}-{Guid.NewGuid():N}");

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class CancellationMaterialiser(CancellationTokenSource Cancellation) : ICommitInstallOperations
    {
        internal bool CancelDuringMaterialisation { get; set; }

        public void Materialise(CommitSnapshotLease lease, string backupDirectoryPath, string materialisedDirectoryPath, CancellationToken cancellationToken)
        {
            if (CancelDuringMaterialisation)
            {
                Cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            DefaultCommitInstallOperations.Instance.Materialise(lease, backupDirectoryPath, materialisedDirectoryPath, cancellationToken);
        }

        public void MoveDirectory(string sourcePath, string destinationPath) => Directory.Move(sourcePath, destinationPath);
    }

    private sealed class ThrowingMaterialiser : ICommitInstallOperations
    {
        internal bool Enabled { get; set; } = true;

        public void Materialise(CommitSnapshotLease lease, string backupDirectoryPath, string materialisedDirectoryPath, CancellationToken cancellationToken)
        {
            if (Enabled)
                throw new InvalidDataException("injected materialisation failure");
            DefaultCommitInstallOperations.Instance.Materialise(lease, backupDirectoryPath, materialisedDirectoryPath, cancellationToken);
        }

        public void MoveDirectory(string sourcePath, string destinationPath) => Directory.Move(sourcePath, destinationPath);
    }

    private sealed class FailingMoveOperations(int failMoveNumber, int? failEveryMoveFrom = null) : ICommitInstallOperations
    {
        private int _moveCount;
        private bool _enabled;
        internal bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (value)
                    Interlocked.Exchange(ref _moveCount, 0);
            }
        }

        public void Materialise(CommitSnapshotLease lease, string backupDirectoryPath, string materialisedDirectoryPath, CancellationToken cancellationToken) =>
            DefaultCommitInstallOperations.Instance.Materialise(lease, backupDirectoryPath, materialisedDirectoryPath, cancellationToken);

        public void MoveDirectory(string sourcePath, string destinationPath)
        {
            int move = Interlocked.Increment(ref _moveCount);
            if (Enabled && (move == failMoveNumber || (failEveryMoveFrom is int threshold && move >= threshold)))
                throw new IOException($"injected publication failure {move}");
            Directory.Move(sourcePath, destinationPath);
        }
    }
}

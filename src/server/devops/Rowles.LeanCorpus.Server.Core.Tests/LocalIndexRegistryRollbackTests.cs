using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Execution;
using Rowles.LeanCorpus.Server.Core.Runtime;
using Rowles.LeanCorpus.Server.Core.Storage;

namespace Rowles.LeanCorpus.Server.Core.Tests;

[Trait("Area", "Server")]
public sealed class LocalIndexRegistryRollbackTests
{
    [Fact]
    public async Task RegistryPersistenceFailureRollsBackPhysicalCreateAndAllowsRetry()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-registry-persist-failure-{Guid.NewGuid():N}");
        string registryPath = Path.Combine(root, "registry.json");
        PhysicalIndexId id = PhysicalIndexId.New();
        string physicalPath = Path.Combine(root, "indices", id.Value);
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(registryPath);
            ServerCoreOptions options = new() { DataRoot = root };
            using LocalIndexRegistry registry = await LocalIndexRegistry.OpenAsync(root, options, TestContext.Current.CancellationToken);
            IndexRegistration registration = CreateRegistration(id);

            Exception failure = await Assert.ThrowsAnyAsync<Exception>(async () =>
                await registry.CreateAsync(registration, TestContext.Current.CancellationToken));

            Assert.IsNotType<AggregateException>(failure);
            Assert.False(registry.TryGet(registration.Name, out _));
            Assert.False(Directory.Exists(physicalPath));
            Assert.Empty(Directory.EnumerateFiles(root));

            Directory.Delete(registryPath);
            IndexRuntimeEntry retry = await registry.CreateAsync(registration, TestContext.Current.CancellationToken)
                ?? throw new Xunit.Sdk.XunitException("The same registration ID was not available after physical rollback.");
            Assert.Equal(id.Value, retry.Registration.Id);
            Assert.True(Directory.Exists(physicalPath));
            Assert.True(File.Exists(registryPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RegistryPersistenceAndPhysicalRollbackFailuresAreBothReported()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-registry-rollback-{Guid.NewGuid():N}");
        string registryPath = Path.Combine(root, "registry.json");
        PhysicalIndexId id = PhysicalIndexId.New();
        string physicalPath = Path.Combine(root, "indices", id.Value);
        IOException cleanupFailure = new("Injected physical rollback failure.");
        LocalIndexStore? physicalStore = null;
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(registryPath);
            ServerCoreOptions options = new() { DataRoot = root };
            using LocalIndexRegistry registry = await LocalIndexRegistry.OpenAsync(
                root,
                options,
                TestContext.Current.CancellationToken,
                physicalStoreFactory: path => physicalStore = new LocalIndexStore(
                    path,
                    options.CommitInterval,
                    options.RefreshInterval,
                    observer: null,
                    installOperations: null,
                    deleteDirectory: (_, _) => throw cleanupFailure));
            IndexSchema schema = new(
                [new IndexFieldDefinition("content", IndexFieldType.Text, true, true)],
                new Dictionary<string, AnalysisDefinition>());
            IndexTopologySettings topology = new(1, 0);
            MutableIndexSettings settings = new(null, null, "content", null);
            IndexRegistration registration = new(
                "books", id.Value, DateTimeOffset.UtcNow, schema, topology, settings,
                SchemaHash.Compute(schema, topology));

            AggregateException failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                await registry.CreateAsync(registration, TestContext.Current.CancellationToken));

            Assert.Equal(2, failure.InnerExceptions.Count);
            Assert.NotSame(cleanupFailure, failure.InnerExceptions[0]);
            Assert.Same(cleanupFailure, failure.InnerExceptions[1]);
            Assert.False(registry.TryGet("books", out _));
            Assert.True(Directory.Exists(physicalPath));
            LocalIndexStoreDiagnostics diagnostics = await physicalStore!.GetLifecycleDiagnosticsAsync(TestContext.Current.CancellationToken);
            Assert.Empty(diagnostics.Entries);
            Assert.Contains(id.Value, diagnostics.RetainedFailedCreateIds);
            using (File.Open(Path.Combine(physicalPath, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }

            LocalIndexDescriptor descriptor = new(id, schema, registration.SchemaHash, settings, topology);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await physicalStore.OpenAsync(descriptor, LocalIndexOpenMode.ReadWrite, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static IndexRegistration CreateRegistration(PhysicalIndexId id)
    {
        IndexSchema schema = new(
            [new IndexFieldDefinition("content", IndexFieldType.Text, true, true)],
            new Dictionary<string, AnalysisDefinition>());
        IndexTopologySettings topology = new(1, 0);
        MutableIndexSettings settings = new(null, null, "content", null);
        return new IndexRegistration("books", id.Value, DateTimeOffset.UtcNow, schema, topology, settings,
            SchemaHash.Compute(schema, topology));
    }
}

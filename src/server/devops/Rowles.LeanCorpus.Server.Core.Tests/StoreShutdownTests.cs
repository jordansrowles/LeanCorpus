using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Core.Execution;

namespace Rowles.LeanCorpus.Server.Core.Tests;

[Trait("Area", "Server")]
public sealed class StoreShutdownTests
{
    [Fact]
    public async Task ClosedStoreCannotCreateANewPhysicalOwner()
    {
        string root = NewRoot();
        var store = new LocalIndexStore(root, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        LocalIndexHandle? unexpected = null;
        try
        {
            await store.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                unexpected = await store.CreateAsync(Descriptor(), cancellationToken: TestContext.Current.CancellationToken));
        }
        finally
        {
            if (unexpected is not null) await unexpected.DisposeAsync();
            await store.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FailedIndexCloseDoesNotSkipOtherPhysicalOwnersOrBecomeSuccessOnRetry()
    {
        string root = NewRoot();
        var store = new LocalIndexStore(root, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        LocalIndexDescriptor firstDescriptor = Descriptor();
        LocalIndexDescriptor secondDescriptor = Descriptor();
        var first = await store.CreateAsync(firstDescriptor, cancellationToken: TestContext.Current.CancellationToken);
        var second = await store.CreateAsync(secondDescriptor, cancellationToken: TestContext.Current.CancellationToken);
        try
        {
            // A stopped writer cannot satisfy this still-pending server sequence.
            // Its final commit fails, but unrelated physical owners must still close.
            first.Runtime.Writer.Dispose();
            first.Runtime.MarkWrite();
            await Assert.ThrowsAnyAsync<Exception>(() => store.DisposeAsync().AsTask());
            string lockPath = Path.Combine(root, secondDescriptor.Id.Value, "write.lock");
            using (File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
            await Assert.ThrowsAnyAsync<Exception>(() => store.DisposeAsync().AsTask());
        }
        finally
        {
            try { await first.DisposeAsync(); } catch { /* The injected final-commit failure remains observable. */ }
            await second.DisposeAsync();
            try { await store.DisposeAsync(); } catch { /* Preserve the tested close failure during cleanup. */ }
            Directory.Delete(root, recursive: true);
        }
    }

    private static LocalIndexDescriptor Descriptor() => new(PhysicalIndexId.New(),
        new IndexSchema([new IndexFieldDefinition("body", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
        "store-shutdown-schema", new MutableIndexSettings(null, null, "body", null));

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "leancorpus-store-shutdown", Guid.NewGuid().ToString("N"));
}

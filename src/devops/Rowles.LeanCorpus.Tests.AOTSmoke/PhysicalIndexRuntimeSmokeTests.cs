using Xunit;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Tests.AOTSmoke;

public sealed class PhysicalIndexRuntimeSmokeTests
{
    [Fact]
    [Trait("Area", "Index")]
    public async Task PhysicalRuntimeDrainsItsSearcherAndReopensUnderNativeAot()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lc-aot-runtime-{Guid.NewGuid():N}");
        var runtime = Open(path);
        PhysicalIndexRuntime.PhysicalSearcherLease? lease = null;
        try
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", "native"));
            using (runtime.EnterOperation()) runtime.Writer.AddDocument(document);
            runtime.Commit(refresh: true);
            lease = runtime.AcquireSearcher();
            Assert.Equal(1, lease.Searcher.Search(new TermQuery("body", "native"), 10, TestContext.Current.CancellationToken).TotalHits);
            Task closing = runtime.CloseAsync().AsTask();
            Assert.False(closing.IsCompleted);
            lease.Dispose();
            lease.Dispose();
            await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var closed = runtime.GetDiagnostics();
            Assert.Equal(PhysicalIndexRuntimeState.Closed, closed.State);
            Assert.False(closed.WriterLockOwned);
            Assert.Equal(0, closed.ActiveMappedViews);
            Assert.Equal(0, closed.DirectoryFileLeases);
            using (File.Open(Path.Combine(path, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
            await using (var reopened = Open(path))
            {
                using var reader = reopened.AcquireSearcher();
                Assert.Equal(1, reader.Searcher.Search(new TermQuery("body", "native"), 10, TestContext.Current.CancellationToken).TotalHits);
            }
            Directory.Move(path, path + "-closed");
            Directory.Delete(path + "-closed", recursive: true);
        }
        finally
        {
            lease?.Dispose();
            await runtime.DisposeAsync();
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    private static PhysicalIndexRuntime Open(string path) => new(path, new IndexWriterConfig { DurableCommits = false },
        new SearcherManagerConfig { RefreshInterval = TimeSpan.FromHours(1) });
}

using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.BrowserApp;

public static partial class BrowserCases
{
    public static async Task<string> RunAsync(string name, Uri baseAddress)
    {
        if (ProbeNames.Contains(name))
            return await RunProbeAsync(name, baseAddress);
        if (name == "interchange")
        {
            using var client = new HttpClient { BaseAddress = baseAddress };
            BrowserFixture.Verify(await client.GetByteArrayAsync("fixture.zip"));
            return Convert.ToBase64String(BrowserFixture.Create());
        }
        if (name == "defaults")
        {
            var config = new IndexWriterConfig();
            Check(!config.DurableCommits, "Browser defaults must explicitly select volatile commits.");
            Check(config.IndexingConcurrency == 1 && config.MaxConcurrentFlushes == 1, "Browser defaults must use serial producers and flushes.");
            Check(config.RamBufferSizeMB <= 32 && config.MaxQueuedBytes <= 32L * 1024 * 1024, "Browser buffer defaults must fit the documented client profile.");
            var search = new IndexSearcherConfig();
            Check(search.MaxCachedSegmentReaders == 32 && search.MaxCachedSegmentReaderBytes == 16L * 1024 * 1024, "Browser cache defaults differ from the documented profile.");
            return "Browser platform defaults";
        }

        string path = Path.Combine("/leancorpus-tests", "case-" + Guid.NewGuid().ToString("N"));
        using var directory = new MMapDirectory(path);
        try
        {
            if (name is "options" or "pending-close" or "merge" or "mutation" or "leases" or "runtime-close")
                return await RunLifecycleAsync(name, directory);
            if (name is "byte-pressure" or "rejection" or "stream-cancellation")
                return await RunAdmissionAsync(name, directory);

            if (name == "durability")
            {
                Expect<PlatformNotSupportedException>(() => new IndexWriter(directory, new IndexWriterConfig { DurableCommits = true }));
                Check(!File.Exists(Path.Combine(path, "write.lock")), "Rejected durable writer retained its lock.");
                using var recovered = new IndexWriter(directory, Settings());
                recovered.AddDocument(Document("usable"));
                recovered.Commit();
                return "Unsupported durability rejected before writer ownership; reopen succeeded";
            }

            using (var writer = new IndexWriter(directory, Settings()))
            {
                if (name == "async")
                {
                    await writer.AddDocumentAsync(Document("one"));
                    await writer.AddDocumentAsync(Document("two"));
                    await writer.AddDocumentsAsync(new[] { Document("three"), Document("four") });
                    await writer.AddDocumentsConcurrentAsync(new[] { Document("five"), Document("six") });
                    await writer.AddDocumentsAsync(Source(), batchSize: 1);
                    await writer.AddDocumentBlockAsync(new[] { Document("child"), Document("parent") });
                    await writer.AddDocumentsConcurrentAsync(Source(), batchSize: 2);
                    await writer.CommitAsync();
                }
                else if (name == "cancellation")
                {
                    using var cancellation = new CancellationTokenSource();
                    cancellation.Cancel();
                    await ExpectAsync<OperationCanceledException>(async () => await writer.AddDocumentAsync(Document("cancelled"), cancellation.Token));
                    await writer.AddDocumentAsync(Document("accepted"));
                    await writer.CommitAsync();
                }
                else throw new ArgumentException("Unknown browser contract case: " + name);
            }
            using var reader = new IndexSearcher(directory);
            Check(reader.Search(new TermQuery("body", "browser"), 20).TotalHits == (name == "async" ? 12 : 1), "Committed query count differs from accepted documents.");
            Check(reader.Search(new TermQuery("id", "cancelled"), 20).TotalHits == 0, "A cancelled document was admitted.");
            return "Accepted commands committed and reopened with exact counts";
        }
        finally
        {
            // The directory owns reader leases; normal test lifetimes close them before deletion.
            directory.Dispose();
            Directory.Delete(path, recursive: true);
        }
    }

    private static IndexWriterConfig Settings() => new()
    {
        DurableCommits = false, IndexingConcurrency = 1, MaxConcurrentFlushes = 1,
        MaxBufferedDocs = 2, MaxQueuedDocs = 2, MaxQueuedBytes = 4096,
        RamBufferSizeMB = 8, RamPerThreadHardLimitMB = 8, MergePolicy = NoMergePolicy.Instance
    };

    private static LeanDocument Document(string id)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        document.Add(new TextField("body", "browser search document"));
        return document;
    }

    private static async IAsyncEnumerable<LeanDocument> Source([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return Document("seven");
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return Document("eight");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

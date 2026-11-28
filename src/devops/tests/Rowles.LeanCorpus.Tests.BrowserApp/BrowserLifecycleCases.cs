using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.BrowserApp;

public static partial class BrowserCases
{
    private static async Task<string> RunLifecycleAsync(string name, MMapDirectory directory)
    {
        if (name == "runtime-close")
        {
            string path = Path.Combine(directory.DirectoryPath, "runtime");
            var runtime = new PhysicalIndexRuntime(path, new IndexWriterConfig());
            using (var operation = runtime.EnterOperation())
            {
                runtime.Writer.AddDocument(Document("owned"));
                runtime.CommitUnderOperation(operation, refresh: true);
            }
            var lease = runtime.AcquireLease();
            Task close = runtime.CloseAsync().AsTask();
            try
            {
                Check(!close.IsCompleted, "Close reclaimed a retained generation.");
                Expect<InvalidOperationException>(runtime.Dispose);
                Check(Count(lease.Searcher) == 1, "Quiescing invalidated a retained searcher.");
            }
            finally { lease.Dispose(); await close; }
            runtime.Dispose();
            using var reopenedDirectory = new MMapDirectory(path);
            using var reopenedWriter = new IndexWriter(reopenedDirectory, new IndexWriterConfig());
            reopenedWriter.Commit();
            return "Composed asynchronous close drains leases and allows immediate reopen";
        }

        if (name == "options")
        {
            foreach (var config in new[]
            {
                new IndexWriterConfig { IndexingConcurrency = 2 },
                new IndexWriterConfig { MaxConcurrentFlushes = 2 },
                new IndexWriterConfig { MaxConcurrentMerges = 2 },
                new IndexWriterConfig { CompressionPolicy = FieldCompressionPolicy.Brotli }
            })
                Expect<PlatformNotSupportedException>(() => new IndexWriter(directory, config));
            Expect<PlatformNotSupportedException>(() => new IndexSearcher(directory, new IndexSearcherConfig { ParallelSearch = true }));
            Expect<PlatformNotSupportedException>(() => new SlowQueryLog(0, TextWriter.Null));
            string logPath = Path.Combine(directory.DirectoryPath, "unsupported.log");
            Expect<PlatformNotSupportedException>(() => SlowQueryLog.ToFile(0, logPath));
            Check(!File.Exists(logPath), "Rejected log created a file.");
            Expect<PlatformNotSupportedException>(() => DirectoryFsync.Sync(directory.DirectoryPath, strict: true));
            return "Unsupported options rejected before allocating resources";
        }

        if (name == "leases")
        {
            // Exercise the actual mapping owner used by compound members and retained cursors.
            using (var output = directory.CreateOutput("lease.bin")) output.WriteByte(42);
            var input = directory.OpenInput("lease.bin");
            using (var child = input.OpenSharedSlice(0, 1))
            {
                Expect<InvalidOperationException>(input.Dispose);
                Check(child.ReadByte() == 42, "Failed parent close invalidated its retained child.");
            }
            input.Dispose();
            input.Dispose();
            using var reopened = directory.OpenInput("lease.bin");
            Check(reopened.ReadByte() == 42, "Retrying close damaged the mapping file.");
            return "Retained mapping rejects blocking close, releases and closes on retry";
        }

        var settings = Settings();
        if (name == "merge")
        {
            settings.UseCompoundFile = true;
            settings.MergePolicy = new TieredMergePolicy(2);
            settings.MaxBufferedDocs = 1;
            using var writer = new IndexWriter(directory, settings);
            await writer.AddDocumentsAsync(new[] { Document("first"), Document("second") });
            await writer.CommitAsync();
            using var historical = new IndexSearcher(directory);
            await writer.AddDocumentsConcurrentAsync(new[] { Document("third"), Document("fourth") });
            await writer.CommitAsync();
            Check(writer.GetNrtSegments().Count < 4, "Automatic merges did not adopt completed output inline.");
            writer.ForceMerge(1);
            writer.Commit();
            using var latest = new IndexSearcher(directory);
            Check(Count(historical) == 2 && Count(latest) == 4, "Merge broke historical reader isolation.");
            Check(directory.ListAll().Any(file => file.EndsWith(".cfs", StringComparison.Ordinal)), "Compound merge output is missing.");
            using var manager = new SearcherManager(directory);
            using var oldLease = manager.AcquireLease();
            await writer.AddDocumentAsync(Document("fifth"));
            await writer.CommitAsync();
            Check(await manager.MaybeRefreshAsync(), "Manager did not refresh to the new commit.");
            using var latestLease = manager.AcquireLease();
            Check(Count(oldLease.Searcher) == 4 && Count(latestLease.Searcher) == 5, "Refresh changed a retained searcher.");
            return "Inline automatic/explicit compound merges and retained reader refresh";
        }

        if (name == "mutation")
        {
            using var writer = new IndexWriter(directory, settings);
            writer.AddDocument(Document("accepted"));
            settings.DurableCommits = true;
            Expect<PlatformNotSupportedException>(writer.Commit);
            Expect<PlatformNotSupportedException>(() => writer.AddDocument(Document("rejected")));
            Expect<PlatformNotSupportedException>(() => writer.GetNrtSegments());
            settings.DurableCommits = false;
            settings.MaxConcurrentFlushes = 2;
            Expect<PlatformNotSupportedException>(writer.PrepareCommit);
            settings.MaxConcurrentFlushes = 1;
            writer.PrepareCommit();
            writer.Commit();
            using var reader = new IndexSearcher(directory);
            Check(Count(reader) == 1, "Rejected mutable settings admitted work.");
            return "Mutable settings cannot enable durability or parallel execution";
        }

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingWriter = new IndexWriter(directory, settings);
        Task pending = pendingWriter.AddDocumentsAsync(PausedSource(release.Task), batchSize: 1).AsTask();
        try
        {
            Check(!pending.IsCompleted, "Source did not suspend as required.");
            Expect<InvalidOperationException>(pendingWriter.Dispose);
            Expect<ObjectDisposedException>(() => pendingWriter.AddDocument(Document("late")));
        }
        finally
        {
            release.TrySetResult();
            await ExpectAsync<ObjectDisposedException>(() => pending);
            pendingWriter.Dispose();
        }
        using (var reopened = new IndexWriter(directory, settings))
        {
            reopened.AddDocument(Document("reopened"));
            reopened.Commit();
        }
        using (var reader = new IndexSearcher(directory)) Check(Count(reader) == 1, "Close published uncommitted source work.");
        return "Suspended source closes without blocking and releases writer ownership on retry";
    }

    private static int Count(IndexSearcher reader) => reader.Search(new TermQuery("body", "browser"), 20).TotalHits;

    private static async IAsyncEnumerable<LeanDocument> PausedSource(Task release)
    {
        yield return Document("before-await");
        await release;
        yield return Document("after-await");
    }
}

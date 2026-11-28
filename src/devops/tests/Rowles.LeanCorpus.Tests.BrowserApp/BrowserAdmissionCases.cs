using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.BrowserApp;

public static partial class BrowserCases
{
    private static async Task<string> RunAdmissionAsync(string name, MMapDirectory directory)
    {
        var settings = Settings();
        if (name == "byte-pressure")
        {
            settings.MaxQueuedBytes = 1;
            settings.MaxBufferedDocs = 1000;
            settings.MaxQueuedDocs = 0;
            using var writer = new IndexWriter(directory, settings);
            for (int i = 0; i < 20; i++) await writer.AddDocumentAsync(Document(i.ToString()));
            await writer.CommitAsync();
            Check(writer.PendingFlushBytes == 0 && writer.ActiveFlushCount == 0, "Inline flush retained pending byte ownership.");
            using var reader = new IndexSearcher(directory);
            Check(Count(reader) == 20, "Byte-pressure flush lost accepted documents.");
            return "Byte admission flushes inline without a worker or pending-byte leak";
        }

        if (name == "rejection")
        {
            settings.MaxTokensPerDocument = 1;
            settings.TokenBudgetPolicy = TokenBudgetPolicy.Reject;
            using var writer = new IndexWriter(directory, settings);
            await writer.AddDocumentAsync(OneTokenDocument());
            await ExpectAsync<TokenBudgetExceededException>(async () => await writer.AddDocumentBlockAsync(new[] { OneTokenDocument(), Document("rejected-parent") }));
            await ExpectAsync<TokenBudgetExceededException>(async () => await writer.AddDocumentsConcurrentAsync(new[] { Document("rejected-batch") }));
            await writer.AddDocumentAsync(OneTokenDocument());
            await writer.CommitAsync();
            using var reader = new IndexSearcher(directory);
            Check(Count(reader) == 2, "Rejected block was partially admitted or poisoned later writes.");
            return "Block and concurrent-batch rejection leave the writer usable";
        }

        using var cancellation = new CancellationTokenSource();
        using (var writer = new IndexWriter(directory, settings))
        {
            await ExpectAsync<OperationCanceledException>(async () =>
                await writer.AddDocumentsAsync(CancelledSource(cancellation), batchSize: 1, cancellation.Token));
            await ExpectAsync<OperationCanceledException>(() => writer.CommitAsync(cancellation.Token));
            await writer.CommitAsync();
        }
        using (var reader = new IndexSearcher(directory)) Check(Count(reader) == 1, "Cancellation rolled back accepted work or admitted another batch.");
        return "Stream cancellation retains accepted work and rejects the next batch";
    }

    private static LeanDocument OneTokenDocument()
    {
        var document = new LeanDocument();
        document.Add(new TextField("body", "browser"));
        return document;
    }

    private static async IAsyncEnumerable<LeanDocument> CancelledSource(
        CancellationTokenSource cancellation, [EnumeratorCancellation] CancellationToken token = default)
    {
        yield return Document("accepted-before-cancel");
        await Task.Yield();
        cancellation.Cancel();
        token.ThrowIfCancellationRequested();
        yield return Document("not-accepted");
    }
}

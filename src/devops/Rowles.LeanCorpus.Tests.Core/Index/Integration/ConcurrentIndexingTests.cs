using System.Collections.Concurrent;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Highlighting;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Contains unit tests for Concurrent Indexing.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class ConcurrentIndexingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ll-conc-{Guid.NewGuid():N}");

    public ConcurrentIndexingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>
    /// Verifies the Add Documents Concurrent: All Docs Searchable scenario.
    /// </summary>
    [Fact(DisplayName = "Add Documents Concurrent: All Docs Searchable")]
    public void AddDocumentsConcurrent_AllDocsSearchable()
    {
        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 500 });

        var docs = new List<LeanDocument>();
        for (int i = 0; i < 100; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("id", i.ToString()));
            doc.Add(new TextField("body", $"document number {i} with searchable content"));
            docs.Add(doc);
        }

        writer.AddDocumentsConcurrent(docs);
        writer.Commit();

        using var searcher = new IndexSearcher(directory);
        var results = searcher.Search(new TermQuery("body", "document"), 200, TestContext.Current.CancellationToken);

        Assert.Equal(100, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Add Documents Concurrent: Preserves Stored Fields scenario.
    /// </summary>
    [Fact(DisplayName = "Add Documents Concurrent: Preserves Stored Fields")]
    public void AddDocumentsConcurrent_PreservesStoredFields()
    {
        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 500 });

        var docs = new List<LeanDocument>();
        for (int i = 0; i < 50; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("id", i.ToString()));
            doc.Add(new TextField("body", "hello world"));
            docs.Add(doc);
        }

        writer.AddDocumentsConcurrent(docs);
        writer.Commit();

        using var searcher = new IndexSearcher(directory);
        var results = searcher.Search(new TermQuery("body", "hello"), 100, TestContext.Current.CancellationToken);

        Assert.Equal(50, results.TotalHits);

        // Verify stored fields for first hit
        var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        Assert.True(stored.ContainsKey("id"));
        Assert.True(stored.ContainsKey("body"));
        Assert.True(stored["id"].Count > 0);
        Assert.True(stored["body"].Count > 0);
    }

    /// <summary>
    /// Verifies the Add Documents Concurrent: Empty Batch No-op scenario.
    /// </summary>
    [Fact(DisplayName = "Add Documents Concurrent: Empty Batch No-op")]
    public void AddDocumentsConcurrent_EmptyBatch_NoOp()
    {
        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());

        writer.AddDocumentsConcurrent([]);
        writer.Commit();

        using var searcher = new IndexSearcher(directory);
        var results = searcher.Search(new TermQuery("body", "anything"), 10, TestContext.Current.CancellationToken);
        Assert.Equal(0, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Add Documents Concurrent: With Numeric Fields scenario.
    /// </summary>
    [Fact(DisplayName = "Add Documents Concurrent: With Numeric Fields")]
    public void AddDocumentsConcurrent_WithNumericFields()
    {
        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 500 });

        var docs = new List<LeanDocument>();
        for (int i = 0; i < 30; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "product listing"));
            doc.Add(new NumericField("price", i * 10.0));
            docs.Add(doc);
        }

        writer.AddDocumentsConcurrent(docs);
        writer.Commit();

        using var searcher = new IndexSearcher(directory);
        var results = searcher.Search(new TermQuery("body", "product"), 100, TestContext.Current.CancellationToken);

        Assert.Equal(30, results.TotalHits);
    }

    /// <summary>
    /// Regression test for C1: DWPT-local doc IDs were set to the global batch index,
    /// causing overlapping ID ranges across partitions and corrupt stored fields / postings.
    /// </summary>
    [Fact(DisplayName = "Add Documents Concurrent: Produces Contiguous Doc IDs And Stored Fields Match Postings")]
    public void AddDocumentsConcurrent_ProducesContiguousDocIds_AndStoredFieldsMatchPostings()
    {
        const int DocCount = 5_000;

        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = DocCount + 1 });

        var docs = new List<LeanDocument>(DocCount);
        for (int i = 0; i < DocCount; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("id", i.ToString()));
            doc.Add(new TextField("body", $"uniqueterm{i} shared"));
            docs.Add(doc);
        }

        writer.AddDocumentsConcurrent(docs);
        writer.Commit();

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(DocCount, searcher.Stats.LiveDocCount);

        // Every unique term must resolve to exactly one document whose stored id matches
        for (int i = 0; i < DocCount; i++)
        {
            var hits = searcher.Search(new TermQuery("body", $"uniqueterm{i}"), 10, TestContext.Current.CancellationToken);
            Assert.True(hits.TotalHits == 1,
                $"Expected exactly 1 hit for uniqueterm{i}, got {hits.TotalHits}");

            var stored = searcher.GetStoredFields(hits.ScoreDocs[0].DocId);
            Assert.True(stored.TryGetValue("id", out var idValues) && idValues.Count == 1,
                $"Missing stored 'id' for uniqueterm{i}");
            Assert.Equal(i.ToString(), idValues![0]);
        }
    }

    /// <summary>
    /// Verifies the Add Documents Concurrent: Preserves Field Lengths For BM25 Scoring scenario.
    /// </summary>
    [Fact(DisplayName = "Add Documents Concurrent: Preserves Field Lengths For BM25 Scoring")]
    public void AddDocumentsConcurrent_PreservesFieldLengthsForBm25Scoring()
    {
        const int DocCount = 200;

        var singlePath = Path.Combine(_dir, "single");
        var concurrentPath = Path.Combine(_dir, "concurrent");
        Directory.CreateDirectory(singlePath);
        Directory.CreateDirectory(concurrentPath);

        var docs = new List<LeanDocument>(DocCount);
        for (int i = 0; i < DocCount; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("id", i.ToString()));
            doc.Add(new TextField("body", BuildBody(i)));
            docs.Add(doc);
        }

        var singleScores = IndexAndScoreSingleThreaded(singlePath, docs);
        var concurrentScores = IndexAndScoreConcurrent(concurrentPath, docs);

        Assert.Equal(singleScores.Count, concurrentScores.Count);
        foreach (var (id, score) in singleScores)
        {
            Assert.True(concurrentScores.TryGetValue(id, out var concurrentScore));
            Assert.Equal(score, concurrentScore, precision: 5);
        }
    }

    /// <summary>
    /// Verifies that bounded commits made between concurrent producer batches preserve every document.
    /// </summary>
    [Fact(DisplayName = "Concurrent Producers: Commits During Production Preserve All Documents", Timeout = 30_000)]
    public async Task ConcurrentProducers_CommitsBetweenProducerBatches_AllCommittedDocsSearchable()
    {
        const int ProducerCount = 4;
        const int Batches = 5;
        const int DocsPerBatch = 10;
        const int DocsPerProducer = Batches * DocsPerBatch;
        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 1_000,
            DurableCommits = false,
            MergePolicy = NoMergePolicy.Instance,
        });

        var errors = new ConcurrentBag<Exception>();
        var batchReady = Enumerable.Range(0, Batches)
            .Select(static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var readyCounts = new int[Batches];
        var nextBatch = Enumerable.Range(0, Batches)
            .Select(static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var producers = Enumerable.Range(0, ProducerCount)
            .Select(producer => Task.Run(async () =>
            {
                try
                {
                    for (int batch = 0; batch < Batches; batch++)
                    {
                        for (int item = 0; item < DocsPerBatch; item++)
                        {
                            int id = producer * DocsPerProducer + (batch * DocsPerBatch) + item;
                            writer.AddDocument(BuildDoc(id, "shared concurrent"));
                        }

                        if (Interlocked.Increment(ref readyCounts[batch]) == ProducerCount)
                            batchReady[batch].TrySetResult();
                        await nextBatch[batch].Task.WaitAsync(TestContext.Current.CancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }))
            .ToArray();

        Task allProducers = Task.WhenAll(producers);
        for (int batch = 0; batch < Batches; batch++)
        {
            await batchReady[batch].Task.WaitAsync(TestContext.Current.CancellationToken);
            writer.Commit();
            nextBatch[batch].TrySetResult();
        }

        await allProducers;
        writer.Commit();

        Assert.Empty(errors);
        using var searcher = new IndexSearcher(directory);
        var results = searcher.Search(new TermQuery("body", "shared"), 1_000, TestContext.Current.CancellationToken);
        Assert.Equal(ProducerCount * DocsPerProducer, results.TotalHits);
        var actualIds = results.ScoreDocs
            .Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0])
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedIds = Enumerable.Range(0, ProducerCount * DocsPerProducer)
            .Select(static id => id.ToString())
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedIds, actualIds);
    }

    /// <summary>Exercises Commit while AddDocument producers remain active.</summary>
    [Fact(DisplayName = "AddDocument Lock Free: Commit While Producers Active Completes Without Loss", Timeout = 30_000)]
    public async Task AddDocumentLockFree_CommitWhileProducersActive_CompletesWithoutLoss()
    {
        const int ProducerCount = 4;
        const int DocumentsPerProducer = 250;
        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 64,
            DurableCommits = false,
            MergePolicy = NoMergePolicy.Instance,
        });
        using var started = new CountdownEvent(ProducerCount);
        var errors = new ConcurrentBag<Exception>();
        int successfulAdds = 0;

        Task[] producers = Enumerable.Range(0, ProducerCount).Select(producer => Task.Run(() =>
        {
            try
            {
                started.Signal();
                for (int item = 0; item < DocumentsPerProducer; item++)
                {
                    int id = producer * DocumentsPerProducer + item;
                    writer.AddDocument(BuildDoc(id, "overlap commit"));
                    Interlocked.Increment(ref successfulAdds);
                    if ((item & 7) == 0) Thread.Yield();
                }
            }
            catch (Exception exception) { errors.Add(exception); }
        })).ToArray();

        Assert.True(started.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "Producers did not start.");
        Assert.True(SpinWait.SpinUntil(
            () => Volatile.Read(ref successfulAdds) >= 32 && producers.Any(static task => !task.IsCompleted),
            TimeSpan.FromSeconds(5)), "Producers did not remain active long enough to overlap Commit.");

        int overlappingCommits = 0;
        while (overlappingCommits < 3 && producers.Any(static task => !task.IsCompleted))
        {
            writer.Commit();
            overlappingCommits++;
        }

        await Task.WhenAll(producers).WaitAsync(TestContext.Current.CancellationToken);
        writer.Commit();
        Assert.True(overlappingCommits > 0, "No Commit overlapped active producers.");
        Assert.Empty(errors);
        Assert.Equal(ProducerCount * DocumentsPerProducer, successfulAdds);

        using var searcher = new IndexSearcher(directory);
        TopDocs hits = searcher.Search(new TermQuery("body", "overlap"), successfulAdds, TestContext.Current.CancellationToken);
        Assert.Equal(successfulAdds, hits.TotalHits);
        string[] ids = hits.ScoreDocs.Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0]).ToArray();
        Assert.Equal(successfulAdds, ids.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Verifies the Add Documents Concurrent: With Deletes After Commit Live Docs Remain Correct scenario.
    /// </summary>
    [Fact(DisplayName = "Add Documents Concurrent: With Deletes After Commit Live Docs Remain Correct")]
    public void AddDocumentsConcurrent_WithDeletesAfterCommit_LiveDocsRemainCorrect()
    {
        const int DocCount = 120;
        var directory = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = DocCount + 1 }))
        {
            var docs = Enumerable.Range(0, DocCount)
                .Select(i =>
                {
                    var doc = BuildDoc(i, "concurrent deletecheck");
                    doc.Add(new StringField("group", i % 3 == 0 ? "victim" : "keeper"));
                    return doc;
                })
                .ToList();

            writer.AddDocumentsConcurrent(docs);
            writer.Commit();
            writer.DeleteDocuments(new TermQuery("group", "victim"));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);

        Assert.Equal(0, searcher.Search(new TermQuery("group", "victim"), DocCount, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(80, searcher.Search(new TermQuery("group", "keeper"), DocCount, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(80, searcher.Stats.LiveDocCount);
    }

    /// <summary>
    /// Verifies the Concurrent Readers During Writer Commits: Never Throw And Eventually See Committed Docs scenario.
    /// </summary>
    [Fact(DisplayName = "Concurrent Readers During Writer Commits: Never Throw And Eventually See Committed Docs", Timeout = 30_000)]
    public async Task ConcurrentReadersDuringWriterCommits_NeverThrowAndEventuallySeeCommittedDocs()
    {
        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 20,
            DurableCommits = false,
            MergePolicy = NoMergePolicy.Instance,
        });
        writer.AddDocument(BuildDoc(0, "initial reader"));
        writer.Commit();

        using var manager = new SearcherManager(directory);
        var errors = new ConcurrentBag<Exception>();
        using var cts = new CancellationTokenSource();
        using var readersStarted = new CountdownEvent(4);

        var readers = Enumerable.Range(0, 4)
            .Select(_ => Task.Factory.StartNew(async () =>
            {
                readersStarted.Signal();
                while (!cts.Token.IsCancellationRequested)
                {
                    IndexSearcher? searcher = null;
                    try
                    {
                        searcher = manager.Acquire();
                        _ = searcher.Search(new TermQuery("body", "reader"), 100).TotalHits;
                        _ = searcher.Stats.LiveDocCount;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        errors.Add(ex);
                        return;
                    }
                    finally
                    {
                        if (searcher is not null)
                            manager.Release(searcher);
                    }

                    await Task.Yield();
                }
            }, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap())
            .ToArray();

        try
        {
            Assert.True(
                readersStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
                "Concurrent readers did not start.");

            for (int i = 1; i <= 40; i++)
            {
                writer.AddDocument(BuildDoc(i, "reader committed"));
                if (i % 5 == 0)
                {
                    writer.Commit();
                    manager.MaybeRefresh();
                }
            }

            writer.Commit();
            manager.MaybeRefresh();

            Assert.Empty(errors);
            Assert.Equal(41, manager.UsingSearcher(s => s.Search(new TermQuery("body", "reader"), 100).TotalHits));
        }
        finally
        {
            cts.Cancel();
            await Task.WhenAll(readers);
        }
    }

    /// <summary>
    /// Verifies the Add Documents During Merge: All Docs Preserved And Indexed scenario.
    /// </summary>
    [Fact(DisplayName = "Add Documents During Merge: All Docs Preserved And Indexed")]
    public void AddDocumentsDuringMerge_AllDocsPreservedAndIndexed()
    {
        var directory = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 5,
            MergeThreshold = 4,
        }))
        {
            for (int i = 0; i < 20; i++)
                writer.AddDocument(BuildDoc(i, "merge baseline"));
            writer.Commit();

            for (int i = 0; i < 50; i++)
                writer.AddDocument(BuildDoc(1000 + i, "merge concurrent"));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var baseline = searcher.Search(new TermQuery("body", "baseline"), 1000, TestContext.Current.CancellationToken).TotalHits;
        var concurrent = searcher.Search(new TermQuery("body", "concurrent"), 1000, TestContext.Current.CancellationToken).TotalHits;
        Assert.Equal(20, baseline);
        Assert.Equal(50, concurrent);
    }

    private static Dictionary<string, float> IndexAndScoreSingleThreaded(string path, IReadOnlyList<LeanDocument> docs)
    {
        var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = docs.Count + 1 }))
        {
            foreach (var doc in docs)
                writer.AddDocument(doc);
            writer.Commit();
        }

        return ScoreSharedTerm(directory, docs.Count);
    }

    private static Dictionary<string, float> IndexAndScoreConcurrent(string path, IReadOnlyList<LeanDocument> docs)
    {
        var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = docs.Count + 1 }))
        {
            writer.AddDocumentsConcurrent(docs);
            writer.Commit();
        }

        return ScoreSharedTerm(directory, docs.Count);
    }

    private static Dictionary<string, float> ScoreSharedTerm(MMapDirectory directory, int count)
    {
        using var searcher = new IndexSearcher(directory);
        var hits = searcher.Search(new TermQuery("body", "shared"), count);
        var scores = new Dictionary<string, float>(StringComparer.Ordinal);

        foreach (var hit in hits.ScoreDocs)
        {
            var fields = searcher.GetStoredFields(hit.DocId);
            scores[fields["id"][0]] = hit.Score;
        }

        return scores;
    }

    private static string BuildBody(int id)
    {
        var extras = Enumerable.Range(0, id % 11)
            .Select(i => $"extra{id}_{i}");
        return string.Join(' ', extras.Prepend("shared"));
    }

    private static LeanDocument BuildDoc(int id, string body)
    {
        var doc = new LeanDocument();
        doc.Add(new StringField("id", id.ToString()));
        doc.Add(new TextField("body", $"{body} uniquedoc{id}"));
        return doc;
    }
}

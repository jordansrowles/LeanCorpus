using System.Collections.Concurrent;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class SegmentReaderQueryLeaseTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ll-query-lease-{Guid.NewGuid():N}");

    [Fact(DisplayName = "Segment Reader Query Lease: Rejects Cross Thread Disposal And Restores Outer Reader")]
    public void NestedReaders_CrossThreadDisposalIsRejected_OuterScopeIsRestored()
    {
        const int workerCount = 8;
        const int rounds = 3;
        var cancellationToken = TestContext.Current.CancellationToken;

        Directory.CreateDirectory(_path);
        using var directory = new MMapDirectory(_path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            DefaultAnalyser = new WhitespaceAnalyser(),
            MaxBufferedDocs = 1,
            MergePolicy = NoMergePolicy.Instance,
            DurableCommits = false,
        }))
        {
            for (int i = 0; i < 2; i++)
            {
                var document = new LeanDocument();
                document.Add(new TextField("body", $"shared term{i}", stored: false));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig
        {
            EnableQueryCache = false,
            MaxCachedSegmentReaders = 1,
            ParallelSearch = false,
        });

        var readers = searcher.GetSegmentReaders();
        Assert.Equal(2, readers.Count);
        var outerReader = readers[0];
        var innerReader = readers[1];

        var ready = Enumerable.Range(0, rounds).Select(_ => new CountdownEvent(workerCount)).ToArray();
        var resume = Enumerable.Range(0, rounds).Select(_ => new ManualResetEventSlim()).ToArray();
        var innerReleased = Enumerable.Range(0, rounds).Select(_ => new CountdownEvent(workerCount)).ToArray();
        var allowReload = Enumerable.Range(0, rounds).Select(_ => new ManualResetEventSlim()).ToArray();
        var reloaded = Enumerable.Range(0, rounds).Select(_ => new CountdownEvent(workerCount)).ToArray();
        var finishRound = Enumerable.Range(0, rounds).Select(_ => new ManualResetEventSlim()).ToArray();
        var completed = Enumerable.Range(0, rounds).Select(_ => new CountdownEvent(workerCount)).ToArray();
        var innerLeases = new SegmentQueryLease[rounds, workerCount];
        var innerStates = new object?[rounds, workerCount];
        var failures = new ConcurrentQueue<Exception>();
        var threads = new Thread[workerCount];

        for (int worker = 0; worker < workerCount; worker++)
        {
            int workerIndex = worker;
            threads[worker] = new Thread(() =>
            {
                for (int round = 0; round < rounds; round++)
                {
                    bool announced = false;
                    bool releasedSignal = false;
                    bool reloadedSignal = false;
                    bool innerDisposed = false;
                    try
                    {
                        using var outerLease = outerReader.AcquireQueryLease();
                        innerLeases[round, workerIndex] = innerReader.AcquireQueryLease();
                        using (var innerState = innerReader.AcquireReadLease())
                            innerStates[round, workerIndex] = innerState.State;

                        if (outerReader.GetDocFreq("body", "shared") != 1 ||
                            innerReader.GetDocFreq("body", "shared") != 1)
                        {
                            throw new InvalidOperationException("Nested reader queries returned an unexpected count.");
                        }

                        Exception? outOfOrderDisposal = Record.Exception(outerLease.Dispose);
                        if (outOfOrderDisposal is not InvalidOperationException)
                        {
                            throw new InvalidOperationException(
                                $"The outer query lease did not reject out-of-order disposal in round {round}.",
                                outOfOrderDisposal);
                        }

                        ready[round].Signal();
                        announced = true;
                        if (!resume[round].Wait(TimeSpan.FromSeconds(15), cancellationToken))
                            throw new TimeoutException($"Worker {workerIndex} was not resumed for round {round}.");

                        innerLeases[round, workerIndex].Dispose();
                        innerDisposed = true;

                        var outerFieldLengths = outerReader.GetFieldLengthsForQuery("body");
                        if (outerFieldLengths is null || outerFieldLengths.Value.Length != 1 ||
                            outerFieldLengths.Value.Span[0] <= 0)
                        {
                            throw new InvalidOperationException(
                                $"Worker {workerIndex} did not recover the outer reader scope in round {round}.");
                        }

                        innerReleased[round].Signal();
                        releasedSignal = true;
                        if (!allowReload[round].Wait(TimeSpan.FromSeconds(15), cancellationToken))
                            throw new TimeoutException($"Inner-reader reload was not allowed in round {round}.");

                        using (var reloadedInnerState = innerReader.AcquireReadLease())
                        {
                            if (ReferenceEquals(innerStates[round, workerIndex], reloadedInnerState.State))
                            {
                                throw new InvalidOperationException(
                                    $"Worker {workerIndex} reused the evicted inner state in round {round}.");
                            }
                        }

                        if (innerReader.GetDocFreq("body", "shared") != 1)
                            throw new InvalidOperationException("The inner reader returned an unexpected count.");

                        reloaded[round].Signal();
                        reloadedSignal = true;
                        if (!finishRound[round].Wait(TimeSpan.FromSeconds(15), cancellationToken))
                            throw new TimeoutException($"Round {round} was not released for cleanup.");
                    }
                    catch (Exception exception)
                    {
                        failures.Enqueue(exception);
                    }
                    finally
                    {
                        if (!innerDisposed)
                        {
                            try { innerLeases[round, workerIndex].Dispose(); }
                            catch (Exception exception) { failures.Enqueue(exception); }
                        }
                        if (!announced)
                            ready[round].Signal();
                        if (!releasedSignal)
                            innerReleased[round].Signal();
                        if (!reloadedSignal)
                            reloaded[round].Signal();
                        completed[round].Signal();
                    }
                }
            }) { IsBackground = true };
        }

        try
        {
            foreach (var thread in threads)
                thread.Start();

            for (int round = 0; round < rounds; round++)
            {
                Assert.True(
                    ready[round].Wait(TimeSpan.FromSeconds(15), cancellationToken),
                    $"Workers did not reach round {round}.");
                long evictionsBeforeRelease = searcher.SegmentReaderCacheMetrics.EvictionCount;

                for (int worker = 0; worker < workerCount; worker++)
                {
                    Exception? crossThreadDisposal = Record.Exception(() => innerLeases[round, worker].Dispose());
                    if (crossThreadDisposal is not InvalidOperationException)
                    {
                        failures.Enqueue(new InvalidOperationException(
                            $"The inner query lease did not reject cross-thread disposal in round {round}.",
                            crossThreadDisposal));
                    }
                }

                resume[round].Set();
                Assert.True(
                    innerReleased[round].Wait(TimeSpan.FromSeconds(15), cancellationToken),
                    $"Workers did not release their inner leases in round {round}.");
                var releasedMetrics = searcher.SegmentReaderCacheMetrics;
                if (releasedMetrics.EvictionCount <= evictionsBeforeRelease || releasedMetrics.EntryCount != 1)
                {
                    failures.Enqueue(new InvalidOperationException(
                        $"Round {round} expected one entry and a new eviction after inner release; " +
                        $"before={evictionsBeforeRelease}, after={releasedMetrics.EvictionCount}, " +
                        $"entries={releasedMetrics.EntryCount}, workerFailures={failures.Count}."));
                }

                allowReload[round].Set();
                Assert.True(
                    reloaded[round].Wait(TimeSpan.FromSeconds(15), cancellationToken),
                    $"Workers did not reload the inner reader in round {round}.");
                finishRound[round].Set();
                Assert.True(
                    completed[round].Wait(TimeSpan.FromSeconds(15), cancellationToken),
                    $"Workers did not finish round {round}.");
            }
        }
        finally
        {
            foreach (var gate in resume)
                gate.Set();
            foreach (var gate in finishRound)
                gate.Set();
            foreach (var gate in allowReload)
                gate.Set();
            foreach (var thread in threads)
            {
                if (thread is { IsAlive: true })
                    Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "A query-lease worker did not stop.");
            }
            foreach (var gate in ready)
                gate.Dispose();
            foreach (var gate in resume)
                gate.Dispose();
            foreach (var gate in innerReleased)
                gate.Dispose();
            foreach (var gate in allowReload)
                gate.Dispose();
            foreach (var gate in reloaded)
                gate.Dispose();
            foreach (var gate in finishRound)
                gate.Dispose();
            foreach (var gate in completed)
                gate.Dispose();
        }

        Assert.Empty(failures);
    }

    public void Dispose()
    {
        if (Directory.Exists(_path))
            Directory.Delete(_path, recursive: true);
    }
}

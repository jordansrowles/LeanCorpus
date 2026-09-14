using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class Lean9FoundationFixTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public Lean9FoundationFixTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(Timeout = 30_000)]
    public async Task DetachedFlush_ShutdownWaitsForTerminalOwnershipAndReleasesPendingBytes()
    {
        using var firstFlushEntered = new ManualResetEventSlim();
        using var releaseFirstFlush = new ManualResetEventSlim();
        int entered = 0;
        var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(DetachedFlush_ShutdownWaitsForTerminalOwnershipAndReleasesPendingBytes))),
            new IndexWriterConfig
            {
                IndexingConcurrency = 2,
                MaxBufferedDocs = 1,
                MaxConcurrentFlushes = 1,
                PhysicalFlushStarted = () =>
                {
                    if (Interlocked.Increment(ref entered) == 1)
                    {
                        firstFlushEntered.Set();
                        releaseFirstFlush.Wait(TestContext.Current.CancellationToken);
                    }
                }
            });

        Task first = Task.Run(() => writer.AddDocument(Document("first", "alpha")), TestContext.Current.CancellationToken);
        Assert.True(firstFlushEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        long firstPending = Volatile.Read(ref writer.PendingFlushBytes);

        Task second = Task.Run(() => writer.AddDocument(Document("second", "beta")), TestContext.Current.CancellationToken);
        Assert.True(SpinWait.SpinUntil(
            () => Volatile.Read(ref writer.PendingFlushBytes) > firstPending,
            TimeSpan.FromSeconds(10)));

        Task dispose = Task.Run(writer.Dispose, TestContext.Current.CancellationToken);
        Assert.True(SpinWait.SpinUntil(() => writer.IsClosing, TimeSpan.FromSeconds(10)));
        releaseFirstFlush.Set();

        await Task.WhenAll(first, second, dispose).WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, Volatile.Read(ref writer.PendingFlushBytes));
        Assert.Equal(2, Volatile.Read(ref entered));
    }

    [Fact]
    public async Task ConcurrentAsyncTokenRejection_RemainsRecoverableAndUnwrapped()
    {
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(ConcurrentAsyncTokenRejection_RemainsRecoverableAndUnwrapped))),
            new IndexWriterConfig
            {
                IndexingConcurrency = 2,
                MaxTokensPerDocument = 2,
                TokenBudgetPolicy = TokenBudgetPolicy.Reject,
                MaxBufferedDocs = 100
            });

        await Assert.ThrowsAsync<TokenBudgetExceededException>(() => writer.AddDocumentsConcurrentAsync([
            Document("accepted", "one two"),
            Document("rejected", "one two three")
        ], TestContext.Current.CancellationToken).AsTask());

        writer.AddDocument(Document("after", "still usable"));
        writer.Commit();
    }

    [Fact]
    public void ConcurrentFatalFailure_IsUnwrappedAndPoisonsBeforeReconciliation()
    {
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(ConcurrentFatalFailure_IsUnwrappedAndPoisonsBeforeReconciliation))),
            new IndexWriterConfig
            {
                IndexingConcurrency = 4,
                DefaultAnalyser = new ThrowingAnalyser(),
                MaxBufferedDocs = 100
            });

        var failure = Assert.Throws<InvalidOperationException>(() => writer.AddDocumentsConcurrent([
            Document("one", "ordinary"),
            Document("fatal-low", "fatal-low"),
            Document("fatal-high", "fatal-high"),
            Document("two", "ordinary")
        ]));
        Assert.Equal("fatal-low", failure.Message);
        Assert.True(Volatile.Read(ref writer.ActiveDwptBytes) > 0);

        var poisoned = Assert.Throws<InvalidOperationException>(() => writer.AddDocument(Document("after", "ordinary")));
        Assert.Same(failure, poisoned.InnerException);
    }

    [Fact(Timeout = 30_000)]
    public async Task ConcurrentFatalFailure_WaitsForAlreadyAdmittedProducerBeforeAbort()
    {
        using var ordinaryEntered = new ManualResetEventSlim();
        using var releaseOrdinary = new ManualResetEventSlim();
        using var fatalEntered = new ManualResetEventSlim();
        var analyser = new BlockingFailureAnalyser(ordinaryEntered, releaseOrdinary, fatalEntered);
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(ConcurrentFatalFailure_WaitsForAlreadyAdmittedProducerBeforeAbort))),
            new IndexWriterConfig
            {
                IndexingConcurrency = 2,
                DefaultAnalyser = analyser,
                MaxBufferedDocs = 100
            });

        Task ordinary = Task.Run(() => writer.AddDocument(Document("ordinary", "ordinary")), TestContext.Current.CancellationToken);
        Assert.True(ordinaryEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Task fatal = Task.Run(() => writer.AddDocumentsConcurrent([Document("fatal", "fatal")]), TestContext.Current.CancellationToken);
        Assert.True(fatalEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        bool admissionClosed = SpinWait.SpinUntil(
            () =>
            {
                try
                {
                    writer.Commit();
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            },
            TimeSpan.FromSeconds(10));
        Assert.True(admissionClosed, "Fatal concurrent failure did not close new indexing admission.");
        Task completed = await Task.WhenAny(fatal, Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
        Assert.NotSame(fatal, completed);

        releaseOrdinary.Set();
        await ordinary.WaitAsync(TestContext.Current.CancellationToken);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fatal.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal("fatal", failure.Message);
    }

    [Fact(Timeout = 30_000)]
    public async Task OrdinaryFatalFailure_WaitsForAlreadyAdmittedProducerBeforeAbort()
    {
        using var ordinaryEntered = new ManualResetEventSlim();
        using var releaseOrdinary = new ManualResetEventSlim();
        using var fatalEntered = new ManualResetEventSlim();
        var analyser = new BlockingFailureAnalyser(ordinaryEntered, releaseOrdinary, fatalEntered);
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(OrdinaryFatalFailure_WaitsForAlreadyAdmittedProducerBeforeAbort))),
            new IndexWriterConfig
            {
                IndexingConcurrency = 2,
                DefaultAnalyser = analyser,
                MaxBufferedDocs = 100
            });

        Task ordinary = Task.Run(() => writer.AddDocument(Document("ordinary", "ordinary")), TestContext.Current.CancellationToken);
        Assert.True(ordinaryEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Task fatal = Task.Run(() => writer.AddDocument(Document("fatal", "fatal")), TestContext.Current.CancellationToken);
        Assert.True(fatalEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        bool admissionClosed = SpinWait.SpinUntil(
            () =>
            {
                try
                {
                    writer.Commit();
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            },
            TimeSpan.FromSeconds(10));
        Assert.True(admissionClosed, "Fatal ordinary failure did not close new indexing admission.");
        Task completed = await Task.WhenAny(fatal, Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
        Assert.NotSame(fatal, completed);

        releaseOrdinary.Set();
        await ordinary.WaitAsync(TestContext.Current.CancellationToken);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fatal.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal("fatal", failure.Message);
    }

    [Fact(Timeout = 30_000)]
    public async Task SimultaneousFatalOperations_DoNotDeadlockAndAbortOnce()
    {
        using var barrier = new Barrier(2);
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(SimultaneousFatalOperations_DoNotDeadlockAndAbortOnce))),
            new IndexWriterConfig
            {
                IndexingConcurrency = 2,
                DefaultAnalyser = new SimultaneousFatalAnalyser(barrier),
                MaxBufferedDocs = 100
            });

        Task first = Task.Run(() => writer.AddDocumentsConcurrent([Document("first", "fatal-one")]), TestContext.Current.CancellationToken);
        Task second = Task.Run(() => writer.AddDocumentsConcurrent([Document("second", "fatal-two")]), TestContext.Current.CancellationToken);
        Task both = Task.WhenAll(first, second);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await both.WaitAsync(TestContext.Current.CancellationToken));
        Assert.True(first.IsFaulted);
        Assert.True(second.IsFaulted);
        Assert.Throws<InvalidOperationException>(() => writer.AddDocument(Document("after", "ordinary")));
    }

    [Fact]
    public void MaintainedBuiltInAnalysers_SatisfyConcurrentOwnershipContract()
    {
        IAnalyser[] analysers =
        [
            new StemmedAnalyser(),
            StemmerAnalyser.Porter(),
            new IcuAnalyser(),
            AnalyserFactory.Create("de")
        ];

        for (int i = 0; i < analysers.Length; i++)
        {
            using var writer = new IndexWriter(new MMapDirectory(SubDir($"built_in_{i}")), new IndexWriterConfig
            {
                IndexingConcurrency = 2,
                DefaultAnalyser = analysers[i],
                MaxBufferedDocs = 100
            });
            writer.AddDocumentsConcurrent([Document("one", "laufen"), Document("two", "laufen")]);
            writer.Commit();
        }
    }

    [Fact]
    public void ConfiguredDefaultAndPerFieldAnalysers_AreIndependentlyOwnedAndPreserveConfiguration()
    {
        var defaultAnalyser = new RecordingConfiguredAnalyser("default-configuration");
        var fieldAnalyser = new RecordingConfiguredAnalyser("field-configuration");

        using var writer = new IndexWriter(
            new MMapDirectory(SubDir(nameof(ConfiguredDefaultAndPerFieldAnalysers_AreIndependentlyOwnedAndPreserveConfiguration))),
            new IndexWriterConfig
            {
                IndexingConcurrency = 3,
                DefaultAnalyser = defaultAnalyser,
                FieldAnalysers = new Dictionary<string, IAnalyser> { ["special"] = fieldAnalyser }
            });

        Assert.Equal(3, defaultAnalyser.OwnedInstances.Count);
        Assert.Equal(3, fieldAnalyser.OwnedInstances.Count);
        Assert.Equal(3, defaultAnalyser.OwnedInstances.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(3, fieldAnalyser.OwnedInstances.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.All(defaultAnalyser.OwnedInstances, owned => Assert.Equal("default-configuration", owned.Configuration));
        Assert.All(fieldAnalyser.OwnedInstances, owned => Assert.Equal("field-configuration", owned.Configuration));
    }

    [Fact(Timeout = 30_000)]
    public async Task ConcurrentAsyncBatch_OwnsOneIndexingOperation()
    {
        using var flushEntered = new ManualResetEventSlim();
        using var releaseFlush = new ManualResetEventSlim();
        IndexWriter? writer = null;
        int observedOperations = 0;
        writer = new IndexWriter(new MMapDirectory(SubDir(nameof(ConcurrentAsyncBatch_OwnsOneIndexingOperation))),
            new IndexWriterConfig
            {
                IndexingConcurrency = 2,
                MaxBufferedDocs = 1,
                PhysicalFlushStarted = () =>
                {
                    observedOperations = writer!.InFlightIndexingOperationsForTests;
                    flushEntered.Set();
                    releaseFlush.Wait(TestContext.Current.CancellationToken);
                }
            });

        using var ownedWriter = writer;
        ValueTask indexing = writer!.AddDocumentsConcurrentAsync(
            [Document("one", "alpha"), Document("two", "beta")],
            TestContext.Current.CancellationToken);
        Assert.True(flushEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(1, observedOperations);
        releaseFlush.Set();
        await indexing;
    }

    [Theory(Timeout = 30_000)]
    [InlineData(1)]
    [InlineData(2)]
    public void PhysicalFlushConcurrency_DoesNotExceedConfiguredLimit(int limit)
    {
        using var release = new ManualResetEventSlim(limit == 1);
        int active = 0;
        int observedMaximum = 0;
        using var writer = new IndexWriter(new MMapDirectory(SubDir($"flush_limit_{limit}")), new IndexWriterConfig
        {
            IndexingConcurrency = 4,
            MaxBufferedDocs = 1,
            MaxConcurrentFlushes = limit,
            PhysicalFlushStarted = () =>
            {
                int current = Interlocked.Increment(ref active);
                UpdateMaximum(ref observedMaximum, current);
                if (current == limit)
                    release.Set();
                release.Wait(TestContext.Current.CancellationToken);
            },
            PhysicalFlushCompleted = () => Interlocked.Decrement(ref active)
        });

        writer.AddDocumentsConcurrent(Enumerable.Range(0, 12)
            .Select(i => Document(i.ToString(System.Globalization.CultureInfo.InvariantCulture), "flush"))
            .ToArray());

        // Admission is detached. Commit supplies the barrier which waits for
        // all physical work and verifies the observed limit.
        writer.Commit();

        Assert.InRange(observedMaximum, 1, limit);
        if (limit > 1)
            Assert.Equal(limit, observedMaximum);
    }

    [Fact(Timeout = 30_000)]
    public void AutomaticFlush_SubmitsDetachedWorkWithoutBlockingTheProducer()
    {
        using var flushEntered = new ManualResetEventSlim();
        using var releaseFlush = new ManualResetEventSlim();
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(AutomaticFlush_SubmitsDetachedWorkWithoutBlockingTheProducer))),
            new IndexWriterConfig
            {
                MaxBufferedDocs = 1,
                PhysicalFlushStarted = () =>
                {
                    flushEntered.Set();
                    releaseFlush.Wait(TestContext.Current.CancellationToken);
                }
            });

        writer.AddDocument(Document("one", "detached"));

        Assert.True(flushEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.True(Volatile.Read(ref writer.PendingFlushBytes) > 0);
        releaseFlush.Set();
        writer.Commit();
    }

    [Fact(Timeout = 30_000)]
    public async Task CoordinatorSubmission_ReservesOrderAtomicallyWithPendingQueueInsertion()
    {
        using var firstSubmissionEntered = new ManualResetEventSlim();
        using var releaseFirstSubmission = new ManualResetEventSlim();
        int submissions = 0;
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(CoordinatorSubmission_ReservesOrderAtomicallyWithPendingQueueInsertion))),
            new IndexWriterConfig
            {
                MaxConcurrentFlushes = 1,
                FlushSubmissionReserved = () =>
                {
                    if (Interlocked.Increment(ref submissions) == 1)
                    {
                        firstSubmissionEntered.Set();
                        releaseFirstSubmission.Wait(TestContext.Current.CancellationToken);
                    }
                }
            });

        DwptFlushBatch CreateBatch(string id)
        {
            var dwpt = new DocumentsWriterPerThread(writer.DefaultAnalyser, new Dictionary<string, IAnalyser>(), writer.Config);
            dwpt.AddDocument(Document(id, "ordered"));
            lock (dwpt)
                return DwptFlushBatch.CaptureFrom(dwpt);
        }

        int firstOrdinal = -1;
        int secondOrdinal = -1;
        Task first = Task.Run(() => firstOrdinal = writer.FlushCoordinator.Submit(CreateBatch("first"), writer.CommitGeneration),
            TestContext.Current.CancellationToken);
        Assert.True(firstSubmissionEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Task second = Task.Run(() => secondOrdinal = writer.FlushCoordinator.Submit(CreateBatch("second"), writer.CommitGeneration),
            TestContext.Current.CancellationToken);
        Assert.False(second.Wait(TimeSpan.FromMilliseconds(100)), "A later submitter passed the reserved-order boundary.");

        releaseFirstSubmission.Set();
        await Task.WhenAll(first, second).WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, firstOrdinal);
        Assert.Equal(1, secondOrdinal);
        writer.Commit();
    }

    [Fact(Timeout = 30_000)]
    public async Task RetainedMemoryPressure_WaitsForPhysicalFlushProgress()
    {
        using var firstFlushEntered = new ManualResetEventSlim();
        using var releaseFlush = new SemaphoreSlim(0);
        int started = 0;
        int completed = 0;
        var config = new IndexWriterConfig
        {
            MaxBufferedDocs = 1,
            MaxConcurrentFlushes = 1,
            MaxQueuedBytes = long.MaxValue,
            PhysicalFlushStarted = () =>
            {
                if (Interlocked.Increment(ref started) == 1)
                    firstFlushEntered.Set();
                releaseFlush.Wait(TestContext.Current.CancellationToken);
            },
            PhysicalFlushCompleted = () => Interlocked.Increment(ref completed)
        };
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(RetainedMemoryPressure_WaitsForPhysicalFlushProgress))), config);

        writer.AddDocument(Document("one", "retained"));
        Assert.True(firstFlushEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        writer.AddDocument(Document("two", "retained"));
        writer.AddDocument(Document("three", "retained"));

        long activeBytes = Volatile.Read(ref writer.ActiveDwptBytes);
        long pendingBytes = Volatile.Read(ref writer.PendingFlushBytes);
        config.MaxQueuedBytes = activeBytes + (pendingBytes / 4);

        Task add = Task.Run(() => writer.AddDocument(Document("four", "retained")), TestContext.Current.CancellationToken);
        Assert.False(add.Wait(TimeSpan.FromMilliseconds(100)), "Producer advanced despite retained flush memory exceeding its budget.");

        for (int i = 0; i < 3; i++)
        {
            releaseFlush.Release();
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref completed) == i + 1, TimeSpan.FromSeconds(10)));
            Assert.False(add.Wait(TimeSpan.FromMilliseconds(100)), "Producer resumed before retained memory fell below its budget.");
        }

        releaseFlush.Release();
        await add.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, Volatile.Read(ref writer.PendingFlushBytes));
    }

    [Fact(Timeout = 30_000)]
    public void RetainedMemoryPressure_DoesNotWaitForAlreadyCompletedFlush()
    {
        var config = new IndexWriterConfig
        {
            MaxBufferedDocs = 1,
            MaxConcurrentFlushes = 1,
            MaxQueuedBytes = long.MaxValue
        };
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(RetainedMemoryPressure_DoesNotWaitForAlreadyCompletedFlush))), config);
        config.MaxQueuedBytes = Volatile.Read(ref writer.ActiveDwptBytes) + 1;

        for (int i = 0; i < 32; i++)
            writer.AddDocument(Document(i.ToString(System.Globalization.CultureInfo.InvariantCulture), "fast flush"));

        writer.Commit();
    }

    [Fact(Timeout = 30_000)]
    public async Task Dispose_WaitsForEveryAcceptedFlushAfterAnEarlierFailure()
    {
        using var firstFlushEntered = new ManualResetEventSlim();
        using var releaseFirstFlush = new ManualResetEventSlim();
        using var secondFlushEntered = new ManualResetEventSlim();
        using var releaseSecondFlush = new ManualResetEventSlim();
        int executions = 0;
        var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(Dispose_WaitsForEveryAcceptedFlushAfterAnEarlierFailure))),
            new IndexWriterConfig
            {
                MaxBufferedDocs = 1,
                MaxConcurrentFlushes = 2,
                PhysicalFlushStarted = () =>
                {
                    if (Interlocked.Increment(ref executions) == 1)
                    {
                        firstFlushEntered.Set();
                        releaseFirstFlush.Wait(TestContext.Current.CancellationToken);
                        throw new IOException("first detached flush failed");
                    }
                    secondFlushEntered.Set();
                    releaseSecondFlush.Wait(TestContext.Current.CancellationToken);
                }
            });

        writer.AddDocument(Document("first", "failure"));
        Assert.True(firstFlushEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        writer.AddDocument(Document("second", "still accepted"));
        Assert.True(secondFlushEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        releaseFirstFlush.Set();

        Task dispose = Task.Run(writer.Dispose, TestContext.Current.CancellationToken);
        Assert.False(dispose.Wait(TimeSpan.FromMilliseconds(100)), "Dispose released writer resources while accepted flush work remained active.");

        releaseSecondFlush.Set();
        var failure = await Assert.ThrowsAsync<IOException>(async () => await dispose.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal("first detached flush failed", failure.Message);
        Assert.Equal(2, Volatile.Read(ref executions));
    }

    [Fact]
    public void UnknownAnalyserOwnership_FailsAtWriterConstruction()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new IndexWriter(new MMapDirectory(SubDir(nameof(UnknownAnalyserOwnership_FailsAtWriterConstruction))),
                new IndexWriterConfig { DefaultAnalyser = new UnsupportedAnalyser() }));

        Assert.Contains(nameof(IThreadLocalAnalyser), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DetachedFlushBatch_ReturnsOwnedBuffersExactlyOnce()
    {
        var analyser = new StandardAnalyser();
        var dwpt = new DocumentsWriterPerThread(analyser, new Dictionary<string, IAnalyser>(),
            new IndexWriterConfig { DefaultAnalyser = analyser });
        dwpt.AddDocument(Document("one", "alpha beta gamma"));

        DwptFlushBatch batch;
        lock (dwpt)
            batch = DwptFlushBatch.CaptureFrom(dwpt);

        batch.Dispose();
        batch.Dispose();

        Assert.Equal(1, batch.CleanupCountForTests);
        Assert.Empty(batch.PostingAccumulators);
    }

    [Fact]
    public void PhysicalFlushFailure_ReleasesPendingBytesAndPoisonsWriter()
    {
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(PhysicalFlushFailure_ReleasesPendingBytesAndPoisonsWriter))),
            new IndexWriterConfig
            {
                MaxBufferedDocs = 1,
                PhysicalFlushStarted = () => throw new IOException("injected flush failure")
            });

        writer.AddDocument(Document("one", "failure"));
        Assert.Throws<IOException>(writer.Commit);
        Assert.Equal(0, Volatile.Read(ref writer.PendingFlushBytes));
        Assert.Throws<InvalidOperationException>(() => writer.AddDocument(Document("after", "failure")));
    }

    [Fact]
    public void CommitPhysicalFlushFailure_PoisonsWriter()
    {
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(CommitPhysicalFlushFailure_PoisonsWriter))),
            new IndexWriterConfig
            {
                MaxBufferedDocs = 100,
                PhysicalFlushStarted = () => throw new IOException("injected commit flush failure")
            });

        writer.AddDocument(Document("one", "buffered"));
        Assert.Throws<IOException>(writer.Commit);
        Assert.Throws<InvalidOperationException>(() => writer.AddDocument(Document("after", "failure")));
    }

    [Fact]
    public void DocumentBlock_PartialBackpressureAcquisitionFailure_ReleasesLocalPermits()
    {
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(DocumentBlock_PartialBackpressureAcquisitionFailure_ReleasesLocalPermits))),
            new IndexWriterConfig
            {
                MaxQueuedDocs = 2,
                MaxBufferedDocs = 100,
                PhysicalFlushStarted = () => throw new IOException("injected flush failure")
            });
        var semaphore = writer.BackpressureSemaphoreForTests;
        Assert.NotNull(semaphore);

        writer.AddDocument(Document("buffered", "first slot"));
        Assert.Equal(1, semaphore!.CurrentCount);

        writer.AddDocumentBlock([
            Document("child", "second slot"),
            Document("parent", "acquisition fails")
        ]);

        Assert.Equal(0, semaphore.CurrentCount);
        Assert.Throws<IOException>(writer.Commit);
        Assert.Equal(2, semaphore.CurrentCount);
        Assert.Equal(0, Volatile.Read(ref writer.SemaphoreSlotsHeld));
    }

    [Fact]
    public void ConcurrentVectorDimensionRejection_RemainsRecoverable()
    {
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(ConcurrentVectorDimensionRejection_RemainsRecoverable))),
            new IndexWriterConfig { IndexingConcurrency = 2, MaxBufferedDocs = 100 });
        var accepted = Document("accepted", "vector");
        accepted.Add(new VectorField("embedding", new float[] { 1, 2, 3 }));
        writer.AddDocumentsConcurrent([accepted]);

        var rejected = Document("rejected", "vector");
        rejected.Add(new VectorField("embedding", new float[] { 1, 2 }));
        Assert.Throws<ArgumentException>(() => writer.AddDocumentsConcurrent([rejected]));

        writer.AddDocument(Document("after", "usable"));
        writer.Commit();
    }

    private string SubDir(string name)
    {
        string path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static LeanDocument Document(string id, string body)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        document.Add(new TextField("body", body));
        return document;
    }

    private static void UpdateMaximum(ref int target, int candidate)
    {
        int current = Volatile.Read(ref target);
        while (candidate > current)
        {
            int observed = Interlocked.CompareExchange(ref target, candidate, current);
            if (observed == current)
                return;
            current = observed;
        }
    }

    private sealed class ThrowingAnalyser : IThreadLocalAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            if (input.StartsWith("fatal"))
                throw new InvalidOperationException(input.ToString());
            sink.Add(input, 0, input.Length);
        }

        public IAnalyser CreateThreadLocalAnalyser() => new ThrowingAnalyser();
    }

    private sealed class BlockingFailureAnalyser : IThreadLocalAnalyser
    {
        private readonly ManualResetEventSlim _ordinaryEntered;
        private readonly ManualResetEventSlim _releaseOrdinary;
        private readonly ManualResetEventSlim _fatalEntered;

        public BlockingFailureAnalyser(
            ManualResetEventSlim ordinaryEntered,
            ManualResetEventSlim releaseOrdinary,
            ManualResetEventSlim fatalEntered)
        {
            _ordinaryEntered = ordinaryEntered;
            _releaseOrdinary = releaseOrdinary;
            _fatalEntered = fatalEntered;
        }

        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            if (input.SequenceEqual("ordinary".AsSpan()))
            {
                _ordinaryEntered.Set();
                if (!_releaseOrdinary.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The test did not release the admitted producer.");
            }
            else if (input.SequenceEqual("fatal".AsSpan()))
            {
                _fatalEntered.Set();
                throw new InvalidOperationException("fatal");
            }

            sink.Add(input, 0, input.Length);
        }

        public IAnalyser CreateThreadLocalAnalyser() => new BlockingFailureAnalyser(
            _ordinaryEntered,
            _releaseOrdinary,
            _fatalEntered);
    }

    private sealed class SimultaneousFatalAnalyser : IThreadLocalAnalyser
    {
        private readonly Barrier _barrier;

        public SimultaneousFatalAnalyser(Barrier barrier) => _barrier = barrier;

        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            if (input.StartsWith("fatal"))
            {
                if (!_barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The simultaneous fatal operations did not both reach analysis.");
                throw new InvalidOperationException(input.ToString());
            }

            sink.Add(input, 0, input.Length);
        }

        public IAnalyser CreateThreadLocalAnalyser() => new SimultaneousFatalAnalyser(_barrier);
    }

    private sealed class UnsupportedAnalyser : IAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink) => sink.Add(input, 0, input.Length);
    }

    private sealed class RecordingConfiguredAnalyser : IThreadLocalAnalyser
    {
        private readonly List<RecordingConfiguredAnalyser> _ownedInstances;

        public RecordingConfiguredAnalyser(string configuration)
            : this(configuration, [])
        {
        }

        private RecordingConfiguredAnalyser(string configuration, List<RecordingConfiguredAnalyser> ownedInstances)
        {
            Configuration = configuration;
            _ownedInstances = ownedInstances;
        }

        public string Configuration { get; }

        public IReadOnlyList<RecordingConfiguredAnalyser> OwnedInstances => _ownedInstances;

        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink) => sink.Add(input, 0, input.Length);

        public IAnalyser CreateThreadLocalAnalyser()
        {
            var owned = new RecordingConfiguredAnalyser(Configuration, _ownedInstances);
            _ownedInstances.Add(owned);
            return owned;
        }
    }
}

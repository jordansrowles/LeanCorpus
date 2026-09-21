using System.Buffers;
using System.Collections.Concurrent;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Indexer.Postings;
using Rowles.LeanCorpus.Index.Segment;
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

    [Fact(Timeout = 30_000)]
    public void CompletedPhysicalFlush_ReleasesSnapshotBeforePublication()
    {
        using var physicalCompleted = new ManualResetEventSlim();
        using var writer = new IndexWriter(
            new MMapDirectory(SubDir(nameof(CompletedPhysicalFlush_ReleasesSnapshotBeforePublication))),
            new IndexWriterConfig
            {
                IndexingConcurrency = 1,
                MaxBufferedDocs = 1,
                RamBufferSizeMB = 1024,
                PhysicalFlushCompleted = physicalCompleted.Set
            });

        writer.AddDocument(Document("completed", "retained"));
        Assert.True(physicalCompleted.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.True(SpinWait.SpinUntil(
            () => writer.FlushCoordinator.PendingCount == 1
                && writer.FlushCoordinator.RetainedSnapshotCountForTests == 0
                && Volatile.Read(ref writer.PendingFlushBytes) == 0,
            TimeSpan.FromSeconds(10)));
        while (writer.FlushCoordinator.WaitForPhysicalProgress())
        {
        }

        lock (writer.WriteLock)
            writer.FlushCoordinator.PublishCompletedPrefix();

        Assert.Equal(0, writer.FlushCoordinator.PendingCount);
        Assert.Equal(0, writer.FlushCoordinator.RetainedSnapshotCountForTests);
        Assert.Single(writer.CommittedSegments);
    }

    [Fact(Timeout = 30_000)]
    public void FailedPhysicalFlush_ReleasesSnapshotBeforeFailedPublication()
    {
        using var physicalCompleted = new ManualResetEventSlim();
        var writer = new IndexWriter(
            new MMapDirectory(SubDir(nameof(FailedPhysicalFlush_ReleasesSnapshotBeforeFailedPublication))),
            new IndexWriterConfig
            {
                IndexingConcurrency = 1,
                MaxBufferedDocs = 1,
                RamBufferSizeMB = 1024,
                PhysicalFlushStarted = () => throw new IOException("injected snapshot ownership failure"),
                PhysicalFlushCompleted = physicalCompleted.Set
            });

        try
        {
            writer.AddDocument(Document("failed", "retained"));
            Assert.True(physicalCompleted.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.True(SpinWait.SpinUntil(
                () => writer.FlushCoordinator.PendingCount == 1
                    && writer.FlushCoordinator.RetainedSnapshotCountForTests == 0
                    && Volatile.Read(ref writer.PendingFlushBytes) == 0,
                TimeSpan.FromSeconds(10)));
            while (writer.FlushCoordinator.WaitForPhysicalProgress())
            {
            }

            lock (writer.WriteLock)
                Assert.Throws<IOException>(writer.FlushCoordinator.PublishCompletedPrefix);

            Assert.Equal(0, writer.FlushCoordinator.RetainedSnapshotCountForTests);
        }
        finally
        {
            try
            {
                writer.Dispose();
            }
            catch (IOException exception)
            {
                Assert.Equal("injected snapshot ownership failure", exception.Message);
            }
        }
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
        Assert.Equal(2, writer.DwptPool!.Length);

        Task ordinary = StartBlockingProducer(() => writer.AddDocument(Document("ordinary", "ordinary")));
        await WaitForSignalAsync(ordinaryEntered, TestContext.Current.CancellationToken);

        Task fatal = StartBlockingProducer(() => writer.AddDocumentsConcurrent([Document("fatal", "fatal")]));
        await WaitForSignalAsync(fatalEntered, TestContext.Current.CancellationToken);

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
        Assert.Equal(2, writer.DwptPool!.Length);

        Task ordinary = StartBlockingProducer(() => writer.AddDocument(Document("ordinary", "ordinary")));
        await WaitForSignalAsync(ordinaryEntered, TestContext.Current.CancellationToken);

        Task fatal = StartBlockingProducer(() => writer.AddDocument(Document("fatal", "fatal")));
        await WaitForSignalAsync(fatalEntered, TestContext.Current.CancellationToken);

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
        var submissionReserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        IndexWriter? writer = null;
        int observedOperations = 0;
        writer = new IndexWriter(new MMapDirectory(SubDir(nameof(ConcurrentAsyncBatch_OwnsOneIndexingOperation))),
            new IndexWriterConfig
            {
                IndexingConcurrency = 2,
                MaxBufferedDocs = 1,
                FlushSubmissionReserved = () =>
                {
                    observedOperations = writer!.InFlightIndexingOperationsForTests;
                    submissionReserved.TrySetResult(true);
                }
            });

        using var ownedWriter = writer;
        ValueTask indexing = writer!.AddDocumentsConcurrentAsync(
            [Document("one", "alpha"), Document("two", "beta")],
            TestContext.Current.CancellationToken);
        await submissionReserved.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(1, observedOperations);
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

        DwptFlushSnapshot CreateBatch(string id)
        {
            var dwpt = new DocumentsWriterPerThread(writer.DefaultAnalyser, new Dictionary<string, IAnalyser>(), writer.Config);
            dwpt.AddDocument(Document(id, "ordered"));
            lock (dwpt)
                return DwptFlushSnapshot.CaptureFrom(dwpt);
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
    public async Task SegmentOrdinalReservations_AreUniqueAcrossFlushAndMergePaths()
    {
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(SegmentOrdinalReservations_AreUniqueAcrossFlushAndMergePaths))),
            new IndexWriterConfig { MaxConcurrentFlushes = 1 });
        var reservations = new ConcurrentBag<int>();

        DwptFlushSnapshot CreateBatch(int id)
        {
            var dwpt = new DocumentsWriterPerThread(writer.DefaultAnalyser, new Dictionary<string, IAnalyser>(), writer.Config);
            dwpt.AddDocument(Document($"flush-{id}", "value"));
            lock (dwpt)
                return DwptFlushSnapshot.CaptureFrom(dwpt);
        }

        Task flushes = Task.Run(() =>
        {
            for (int i = 0; i < 32; i++)
                reservations.Add(writer.FlushCoordinator.Submit(CreateBatch(i), writer.CommitGeneration));
        }, TestContext.Current.CancellationToken);
        Task merges = Task.Run(() =>
        {
            for (int i = 0; i < 32; i++)
            {
                int start = writer.ReserveSegmentOrdinalRange(8);
                for (int ordinal = start; ordinal < start + 8; ordinal++)
                    reservations.Add(ordinal);
            }
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(flushes, merges).WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(reservations.Count, reservations.Distinct().Count());
        writer.Commit();
    }

    [Fact(Timeout = 30_000)]
    public void SequenceNumberRanges_AreUniqueNonOverlappingAndPersistedOnReopen()
    {
        string path = SubDir(nameof(SequenceNumberRanges_AreUniqueNonOverlappingAndPersistedOnReopen));
        var config = new IndexWriterConfig
        {
            IndexingConcurrency = 2,
            MaxBufferedDocs = 1,
            MaxConcurrentFlushes = 2,
            MergePolicy = NoMergePolicy.Instance,
            TrackSequenceNumbers = true
        };

        using (var writer = new IndexWriter(new MMapDirectory(path), config))
        {
            writer.AddDocumentsConcurrent(Enumerable.Range(0, 20)
                .Select(i => Document(i.ToString(System.Globalization.CultureInfo.InvariantCulture), "sequence"))
                .ToArray());
            writer.Commit();
            AssertSequenceRanges(writer.CommittedSegments, expectedDocumentCount: 20);
            Assert.Equal(20, writer.NextSequenceNumber);
        }

        using var reopened = new IndexWriter(new MMapDirectory(path), config);
        AssertSequenceRanges(reopened.CommittedSegments, expectedDocumentCount: 20);
        Assert.Equal(20, reopened.NextSequenceNumber);
    }

    [Fact]
    public void EmptyCommittedIndex_RestoresSequenceNumberZero()
    {
        string path = SubDir(nameof(EmptyCommittedIndex_RestoresSequenceNumberZero));
        var config = new IndexWriterConfig { TrackSequenceNumbers = true };

        using (var writer = new IndexWriter(new MMapDirectory(path), config))
            writer.Commit();

        using var reopened = new IndexWriter(new MMapDirectory(path), config);
        Assert.Equal(0, reopened.NextSequenceNumber);
    }

    [Fact(Timeout = 30_000)]
    public async Task FatalReconciliation_ReleasesBackpressureForAlreadyAdmittedProducer()
    {
        using var fatalEntered = new ManualResetEventSlim();
        using var releaseFatal = new ManualResetEventSlim();
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(FatalReconciliation_ReleasesBackpressureForAlreadyAdmittedProducer))),
            new IndexWriterConfig
            {
                IndexingConcurrency = 2,
                MaxQueuedDocs = 1,
                MaxBufferedDocs = 100,
                DefaultAnalyser = new BackpressureFailureAnalyser(fatalEntered, releaseFatal)
            });

        Task fatal = StartBlockingProducer(() => writer.AddDocument(Document("fatal", "fatal")));
        await WaitForSignalAsync(fatalEntered, TestContext.Current.CancellationToken);

        Task blocked = StartBlockingProducer(() => writer.AddDocument(Document("blocked", "ordinary")));
        Assert.True(SpinWait.SpinUntil(
            () => writer.InFlightIndexingOperationsForTests == 2,
            TimeSpan.FromSeconds(10)), "The second producer did not enter before fatal reconciliation.");

        releaseFatal.Set();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fatal.WaitAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await blocked.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, Volatile.Read(ref writer.SemaphoreSlotsHeld));
        Assert.Equal(1, writer.BackpressureSemaphoreForTests!.CurrentCount);
        Assert.Throws<InvalidOperationException>(() => writer.AddDocument(Document("after", "ordinary")));
    }

    [Fact]
    public void BlockIndexSortRejectionBeforeMutation_DoesNotPoisonWriter()
    {
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(BlockIndexSortRejectionBeforeMutation_DoesNotPoisonWriter))),
            new IndexWriterConfig
            {
                IndexSort = new IndexSort(Rowles.LeanCorpus.Search.Scoring.SortField.Numeric("rank"))
            });

        Assert.Throws<NotSupportedException>(() => writer.AddDocumentBlock(
        [
            Document("child", "child"),
            Document("parent", "parent")
        ]));

        var accepted = Document("after", "ordinary");
        accepted.Add(new NumericField("rank", 1));
        writer.AddDocument(accepted);
        writer.Commit();
    }

    [Fact]
    public void OversizedBlockRejectionBeforeMutation_DoesNotPoisonWriter()
    {
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(OversizedBlockRejectionBeforeMutation_DoesNotPoisonWriter))),
            new IndexWriterConfig { MaxQueuedDocs = 1 });

        Assert.Throws<InvalidOperationException>(() => writer.AddDocumentBlock(
        [
            Document("child", "child"),
            Document("parent", "parent")
        ]));

        writer.AddDocument(Document("after", "ordinary"));
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
            MaxQueuedBytes = 1
        };
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(RetainedMemoryPressure_DoesNotWaitForAlreadyCompletedFlush))), config);

        for (int i = 0; i < 32; i++)
            writer.AddDocument(Document(i.ToString(System.Globalization.CultureInfo.InvariantCulture), "fast flush"));

        writer.Commit();
    }

    [Fact(Timeout = 30_000)]
    public async Task PublishCompletedPrefix_RemovesSuccessfulStateBeforeLaterFailure()
    {
        using var firstFlushEntered = new ManualResetEventSlim();
        using var secondFlushEntered = new ManualResetEventSlim();
        using var releaseFirstFlush = new ManualResetEventSlim();
        using var releaseSecondFlush = new ManualResetEventSlim();
        int executions = 0;
        int completed = 0;
        using var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(PublishCompletedPrefix_RemovesSuccessfulStateBeforeLaterFailure))),
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
                        return;
                    }

                    secondFlushEntered.Set();
                    releaseSecondFlush.Wait(TestContext.Current.CancellationToken);
                    throw new IOException("second detached flush failed");
                },
                PhysicalFlushCompleted = () => Interlocked.Increment(ref completed)
            });

        writer.AddDocument(Document("first", "success"));
        Assert.True(firstFlushEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        writer.AddDocument(Document("second", "failure"));
        Assert.True(secondFlushEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        releaseFirstFlush.Set();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref completed) == 1, TimeSpan.FromSeconds(10)));
        releaseSecondFlush.Set();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref completed) == 2, TimeSpan.FromSeconds(10)));
        while (writer.FlushCoordinator.WaitForPhysicalProgress())
        {
        }

        lock (writer.WriteLock)
            Assert.Throws<IOException>(writer.FlushCoordinator.PublishCompletedPrefix);

        lock (writer.WriteLock)
            Assert.Throws<IOException>(writer.FlushCoordinator.DrainAndPublish);

        Assert.Single(writer.CommittedSegments);
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

        DwptFlushSnapshot batch;
        lock (dwpt)
            batch = DwptFlushSnapshot.CaptureFrom(dwpt);

        batch.Dispose();
        batch.Dispose();

        Assert.Equal(1, batch.CleanupCountForTests);
        Assert.Equal(0, batch.Postings.AllocatedBytes);
    }

    [Fact]
    public void DetachedFlushFailure_ReturnsInjectedPostingPools()
    {
        var bytePool = new TrackingPool<byte>();
        var statePool = new TrackingPool<PostingTermState>();
        var writer = new IndexWriter(
            new MMapDirectory(SubDir(nameof(DetachedFlushFailure_ReturnsInjectedPostingPools))),
            new IndexWriterConfig
            {
                PhysicalFlushStarted = () => throw new IOException("injected posting flush failure")
            });
        var dwpt = new DocumentsWriterPerThread(
            writer.DefaultAnalyser,
            new Dictionary<string, IAnalyser>(),
            writer.Config,
            statePool,
            bytePool);

        try
        {
            dwpt.AddDocument(Document("one", "alpha beta gamma"));
            DwptFlushSnapshot snapshot;
            lock (dwpt)
                snapshot = DwptFlushSnapshot.CaptureFrom(dwpt);

            writer.FlushCoordinator.Submit(snapshot, commitGeneration: 0);
            lock (writer.WriteLock)
                Assert.Throws<IOException>(writer.FlushCoordinator.DrainAndPublish);

            dwpt.Dispose();
            Assert.Equal(0, bytePool.ActiveCount);
            Assert.Equal(0, statePool.ActiveCount);
            Assert.Equal(0, bytePool.DoubleReturnCount);
            Assert.Equal(0, statePool.DoubleReturnCount);
        }
        finally
        {
            dwpt.Dispose();
            writer.Dispose();
        }
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
    public void DocumentBlock_BackpressureRaceWithFlushFailure_ReleasesAllPermits()
    {
        using var flushStarted = new ManualResetEventSlim();
        using var releaseFlush = new ManualResetEventSlim();
        var writer = new IndexWriter(new MMapDirectory(SubDir(nameof(DocumentBlock_BackpressureRaceWithFlushFailure_ReleasesAllPermits))),
            new IndexWriterConfig
            {
                MaxQueuedDocs = 2,
                MaxBufferedDocs = 100,
                PhysicalFlushStarted = () =>
                {
                    flushStarted.Set();
                    releaseFlush.Wait(TestContext.Current.CancellationToken);
                    throw new IOException("injected flush failure");
                }
            });
        var semaphore = writer.BackpressureSemaphoreForTests;
        Assert.NotNull(semaphore);

        try
        {
            writer.AddDocument(Document("buffered", "first slot"));
            Assert.Equal(1, semaphore!.CurrentCount);

            Task block = Task.Run(() => writer.AddDocumentBlock([
                Document("child", "second slot"),
                Document("parent", "flush failure races with admission")
            ]), TestContext.Current.CancellationToken);

            Assert.True(flushStarted.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Equal(0, semaphore.CurrentCount);
            releaseFlush.Set();

            Exception? blockFailure = Record.Exception(() => block.GetAwaiter().GetResult());
            if (blockFailure is not null)
            {
                var rejection = Assert.IsType<InvalidOperationException>(blockFailure);
                Assert.IsType<IOException>(rejection.InnerException);
            }

            Assert.Throws<IOException>(writer.Commit);
            Assert.Equal(2, semaphore.CurrentCount);
            Assert.Equal(0, Volatile.Read(ref writer.SemaphoreSlotsHeld));
        }
        finally
        {
            releaseFlush.Set();
            try
            {
                writer.Dispose();
            }
            catch (IOException)
            {
                // Commit has already observed the injected physical flush failure.
            }
        }
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

    private static Task StartBlockingProducer(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true
        };
        thread.Start();
        return completion.Task;
    }

    private static async Task WaitForSignalAsync(ManualResetEventSlim signal, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!signal.IsSet && DateTime.UtcNow < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);

        Assert.True(signal.IsSet, "The expected producer did not reach its synchronisation point.");
    }

    private static LeanDocument Document(string id, string body)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        document.Add(new TextField("body", body));
        return document;
    }

    private static void AssertSequenceRanges(IEnumerable<SegmentInfo> segments, int expectedDocumentCount)
    {
        var ranges = segments
            .Select(segment => (segment.MinSequenceNumber, segment.MaxSequenceNumber))
            .OrderBy(range => range.MinSequenceNumber)
            .ToArray();

        Assert.NotEmpty(ranges);
        Assert.All(ranges, range =>
        {
            Assert.NotNull(range.MinSequenceNumber);
            Assert.NotNull(range.MaxSequenceNumber);
        });

        long expectedStart = 0;
        foreach (var (start, end) in ranges)
        {
            Assert.Equal(expectedStart, start!.Value);
            Assert.True(end!.Value >= start.Value);
            expectedStart = end.Value + 1;
        }
        Assert.Equal(expectedDocumentCount, expectedStart);
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

    private sealed class BackpressureFailureAnalyser : IThreadLocalAnalyser
    {
        private readonly ManualResetEventSlim _fatalEntered;
        private readonly ManualResetEventSlim _releaseFatal;

        public BackpressureFailureAnalyser(ManualResetEventSlim fatalEntered, ManualResetEventSlim releaseFatal)
        {
            _fatalEntered = fatalEntered;
            _releaseFatal = releaseFatal;
        }

        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            if (input.SequenceEqual("fatal".AsSpan()))
            {
                _fatalEntered.Set();
                if (!_releaseFatal.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The test did not release the fatal producer.");
                throw new InvalidOperationException("fatal");
            }

            sink.Add(input, 0, input.Length);
        }

        public IAnalyser CreateThreadLocalAnalyser() => new BackpressureFailureAnalyser(_fatalEntered, _releaseFatal);
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

    private sealed class TrackingPool<T> : ArrayPool<T>
    {
        private readonly HashSet<T[]> _active = [];

        internal int ActiveCount => _active.Count;
        internal int DoubleReturnCount { get; private set; }

        public override T[] Rent(int minimumLength)
        {
            var array = new T[Math.Max(minimumLength, 256)];
            _active.Add(array);
            return array;
        }

        public override void Return(T[] array, bool clearArray = false)
        {
            if (!_active.Remove(array))
                DoubleReturnCount++;
            if (clearArray)
                Array.Clear(array);
        }
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

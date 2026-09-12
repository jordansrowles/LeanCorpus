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

        Assert.InRange(observedMaximum, 1, limit);
        if (limit > 1)
            Assert.Equal(limit, observedMaximum);
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

        Assert.Throws<IOException>(() => writer.AddDocument(Document("one", "failure")));
        Assert.Equal(0, Volatile.Read(ref writer.PendingFlushBytes));
        Assert.Throws<InvalidOperationException>(() => writer.AddDocument(Document("after", "failure")));
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

using BenchmarkDotNet.Attributes;
using System.Diagnostics;
using Rowles.LeanCorpus.Diagnostics;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="IndexWriter.AddDocumentAsync"/> and
/// <see cref="IndexWriter.AddDocumentsAsync(System.Collections.Generic.IReadOnlyList{LeanDocument}, System.Threading.CancellationToken)"/>
/// throughput against synchronous <see cref="IndexWriter.AddDocument"/>.
/// </summary>
/// <remarks>
/// Every method performs the same completed work: ingestion, commit, every merge the default policy
/// selects (published by a follow-up commit) and disposal. Without the merge barrier, disposal cancels
/// merges that have not started and waits for those that have, so methods differed in completed merge
/// work rather than in ingestion cost. <see cref="AsyncIndexingNoMergeBenchmarks"/> remains the
/// ingestion-only control. Set <see cref="MergeCompletionBarrier.StagesEnvironmentVariable"/> to
/// <c>1</c> to print per-iteration stage timings and merge accounting.
/// </remarks>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[WarmupCount(2)]
[IterationCount(5)]
[InvocationCount(1)]
public class AsyncIndexingBenchmarks
{
    private const int RoutineProducerCount = 4;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    /// <summary>Set to <c>1</c> to print opt-in async indexing counters after each iteration.</summary>
    public const string CountersEnvironmentVariable = "LEANCORPUS_ASYNC_INDEX_COUNTERS";

    private LeanDocument[] _documents = [];
    private readonly List<string> _iterationPaths = [];
    private IDisposable? _counterScope;

    [GlobalSetup]
    public void Setup()
    {
        var bodies = BenchmarkData.BuildDocuments(DocumentCount);
        _documents = new LeanDocument[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", bodies[i]));
            _documents[i] = doc;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        CleanupIterationPaths();
        _counterScope?.Dispose();
        _counterScope = null;
    }

    /// <summary>
    /// Opens an async indexing counter scope when <see cref="CountersEnvironmentVariable"/> is
    /// <c>1</c>. Counters stay off by default so ordinary reruns remain comparable with
    /// baselines captured without them.
    /// </summary>
    [IterationSetup]
    public void BeginIterationCounters()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(CountersEnvironmentVariable), "1", StringComparison.Ordinal))
            return;

        _counterScope ??= AsyncIndexingDiagnostics.BeginMeasurement();
        AsyncIndexingDiagnostics.Reset();
    }

    [IterationCleanup]
    public void CleanupIterationPaths()
    {
        MergeCompletionBarrier.ReportLastIteration(DocumentCount);
        if (_counterScope is not null)
        {
            Console.WriteLine(
                $"// Async indexing counters: documents={DocumentCount}, {AsyncIndexingDiagnostics.GetSnapshot()}");
        }

        foreach (var path in _iterationPaths)
        {
            if (IODirectory.Exists(path))
                IODirectory.Delete(path, recursive: true);
        }
        _iterationPaths.Clear();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_AddDocument_Sync()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-async-sync-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var dir = new LeanMMapDirectory(path);
        var stages = MergeCompletionBarrier.Start();
        var writer = new IndexWriter(
            dir,
            new IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        foreach (var doc in _documents)
            writer.AddDocument(doc);
        return MergeCompletionBarrier.CommitCompleteMergesAndDispose(writer, _documents.Length, stages);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<int> LeanCorpus_AddDocumentAsync_Sequential()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-async-seq-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var dir = new LeanMMapDirectory(path);
        var stages = MergeCompletionBarrier.Start();
        var writer = new IndexWriter(
            dir,
            new IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        foreach (var doc in _documents)
            await writer.AddDocumentAsync(doc);
        return MergeCompletionBarrier.CommitCompleteMergesAndDispose(writer, _documents.Length, stages);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<int> LeanCorpus_AddDocumentsAsync_Batch()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-async-batch-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var dir = new LeanMMapDirectory(path);
        var stages = MergeCompletionBarrier.Start();
        var writer = new IndexWriter(
            dir,
            new IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        await writer.AddDocumentsAsync(_documents);
        return MergeCompletionBarrier.CommitCompleteMergesAndDispose(writer, _documents.Length, stages);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<int> LeanCorpus_AddDocumentsConcurrentAsync_Batch()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-async-concurrent-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var dir = new LeanMMapDirectory(path);
        var stages = MergeCompletionBarrier.Start();
        var writer = new IndexWriter(
            dir,
            new IndexWriterConfig { IndexingConcurrency = 2, MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        await writer.AddDocumentsConcurrentAsync(_documents);
        return MergeCompletionBarrier.CommitCompleteMergesAndDispose(writer, _documents.Length, stages);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<int> LeanCorpus_AddDocumentAsync_BoundedConcurrentCallers()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-async-bounded-callers-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var dir = new LeanMMapDirectory(path);
        var stages = MergeCompletionBarrier.Start();
        var writer = new IndexWriter(
            dir,
            new IndexWriterConfig
            {
                MaxQueuedDocs = 20_000,
                MaxBufferedDocs = 10_000,
                RamBufferSizeMB = 256,
            });
        int nextDocument = -1;
        var producers = new Task[RoutineProducerCount];
        for (int i = 0; i < producers.Length; i++)
            producers[i] = AddDocumentsAsync();

        await Task.WhenAll(producers);
        return MergeCompletionBarrier.CommitCompleteMergesAndDispose(writer, _documents.Length, stages);

        async Task AddDocumentsAsync()
        {
            while (true)
            {
                int index = Interlocked.Increment(ref nextDocument);
                if (index >= _documents.Length)
                    return;

                await writer.AddDocumentAsync(_documents[index]);
            }
        }
    }
}

[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[WarmupCount(2)]
[IterationCount(5)]
[InvocationCount(1)]
public class AsyncIndexingCapacityOneStressBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(1_000);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private LeanDocument[] _documents = [];
    private readonly List<string> _iterationPaths = [];

    [GlobalSetup]
    public void Setup()
    {
        var bodies = BenchmarkData.BuildDocuments(DocumentCount);
        _documents = new LeanDocument[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", bodies[i]));
            _documents[i] = doc;
        }
    }

    private IDisposable? _counterScope;

    [GlobalCleanup]
    public void Cleanup()
    {
        CleanupIterationPaths();
        _counterScope?.Dispose();
        _counterScope = null;
    }

    /// <summary>Opens the opt-in counter scope when <see cref="AsyncIndexingBenchmarks.CountersEnvironmentVariable"/> is <c>1</c>.</summary>
    [IterationSetup]
    public void BeginIterationCounters()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(AsyncIndexingBenchmarks.CountersEnvironmentVariable), "1", StringComparison.Ordinal))
            return;

        _counterScope ??= AsyncIndexingDiagnostics.BeginMeasurement();
        AsyncIndexingDiagnostics.Reset();
    }

    [IterationCleanup]
    public void CleanupIterationPaths()
    {
        MergeCompletionBarrier.ReportLastIteration(DocumentCount);
        if (_counterScope is not null)
        {
            Console.WriteLine(
                $"// Async indexing counters: documents={DocumentCount}, {AsyncIndexingDiagnostics.GetSnapshot()}");
        }

        foreach (var path in _iterationPaths)
        {
            if (IODirectory.Exists(path))
                IODirectory.Delete(path, recursive: true);
        }
        _iterationPaths.Clear();
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<int> LeanCorpus_AddDocumentAsync_CapacityOneEagerCallers()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-async-capacity-one-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var dir = new LeanMMapDirectory(path);
        var stages = MergeCompletionBarrier.Start();
        var writer = new IndexWriter(
            dir,
            new IndexWriterConfig { MaxQueuedDocs = 1, MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        var writes = new Task[_documents.Length];
        for (int i = 0; i < _documents.Length; i++)
            writes[i] = writer.AddDocumentAsync(_documents[i]).AsTask();

        await Task.WhenAll(writes);
        return MergeCompletionBarrier.CommitCompleteMergesAndDispose(writer, _documents.Length, stages);
    }
}

/// <summary>
/// Async ingestion with merges disabled. <see cref="AsyncIndexingBenchmarks"/> includes a
/// background merge that writer disposal waits for, which makes its samples bimodal and hides
/// the cost of the async queue itself. This explicit suite keeps the same corpus and writer
/// settings, disables merging and takes more samples, so ingestion paths can be compared.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[WarmupCount(2)]
[IterationCount(15)]
[InvocationCount(1)]
public class AsyncIndexingNoMergeBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private LeanDocument[] _documents = [];
    private readonly List<string> _iterationPaths = [];

    [GlobalSetup]
    public void Setup()
    {
        var bodies = BenchmarkData.BuildDocuments(DocumentCount);
        _documents = new LeanDocument[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", bodies[i]));
            _documents[i] = doc;
        }
    }

    [GlobalCleanup]
    public void Cleanup() => CleanupIterationPaths();

    [IterationCleanup]
    public void CleanupIterationPaths()
    {
        foreach (var path in _iterationPaths)
        {
            if (IODirectory.Exists(path))
                IODirectory.Delete(path, recursive: true);
        }
        _iterationPaths.Clear();
    }

    private static IndexWriterConfig Config() => new()
    {
        MaxBufferedDocs = 10_000,
        RamBufferSizeMB = 256,
        MergePolicy = NoMergePolicy.Instance,
    };

    private string NewPath(string name)
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-async-nomerge-{name}-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _iterationPaths.Add(path);
        return path;
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_AddDocument_Sync()
    {
        using var dir = new LeanMMapDirectory(NewPath("sync"));
        using var writer = new IndexWriter(dir, Config());
        foreach (var doc in _documents)
            writer.AddDocument(doc);
        writer.Commit();
        return _documents.Length;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<int> LeanCorpus_AddDocumentAsync_Sequential()
    {
        using var dir = new LeanMMapDirectory(NewPath("seq"));
        using var writer = new IndexWriter(dir, Config());
        foreach (var doc in _documents)
            await writer.AddDocumentAsync(doc);
        writer.Commit();
        return _documents.Length;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<int> LeanCorpus_AddDocumentsAsync_Batch()
    {
        using var dir = new LeanMMapDirectory(NewPath("batch"));
        using var writer = new IndexWriter(dir, Config());
        await writer.AddDocumentsAsync(_documents);
        writer.Commit();
        return _documents.Length;
    }
}

/// <summary>
/// Benchmark-owned merge barrier for <see cref="AsyncIndexingBenchmarks"/>. ADR007 keeps normal
/// commit independent of background merging, so the barrier lives here rather than in the writer:
/// it commits, waits for scheduled merges, commits again to publish each adopted merge and repeats
/// until the merge policy selects nothing further. Disposal then has no merge left to cancel or join.
/// </summary>
internal static class MergeCompletionBarrier
{
    /// <summary>Set to <c>1</c> to print per-iteration stage timings and merge accounting.</summary>
    public const string StagesEnvironmentVariable = "LEANCORPUS_ASYNC_INDEX_STAGES";

    private const int MaxMergeRounds = 64;

    private static Stages? s_last;

    internal sealed class Stages
    {
        public long Started;
        public long Ingested;
        public long Committed;
        public long MergesCompleted;
        public long Disposed;
        public int MergesStarted;
        public int MergeRounds;
        public int FinalSegments;
        public int LiveDocuments;
    }

    public static Stages Start() => new() { Started = Stopwatch.GetTimestamp() };

    /// <summary>
    /// Commits, completes every policy-selected merge, verifies the final searchable state and disposes
    /// <paramref name="writer"/>. Throws when the completed work differs from the requested workload.
    /// </summary>
    public static int CommitCompleteMergesAndDispose(IndexWriter writer, int expectedDocuments, Stages stages)
    {
        try
        {
            stages.Ingested = Stopwatch.GetTimestamp();
            writer.Config.MergeStarting = () => Interlocked.Increment(ref stages.MergesStarted);
            writer.Commit();
            stages.Committed = Stopwatch.GetTimestamp();

            while (true)
            {
                Task? pending;
                lock (writer.MergeLock)
                    pending = writer.MergeTask;
                if (pending is null)
                    break;

                pending.GetAwaiter().GetResult();
                if (++stages.MergeRounds > MaxMergeRounds)
                    throw new InvalidOperationException("Background merges did not settle within the benchmark barrier.");

                // Publishes the adopted merge and lets the policy schedule any follow-up merge.
                writer.Commit();
            }

            stages.MergesCompleted = Stopwatch.GetTimestamp();
            stages.FinalSegments = writer.CommittedSegments.Count;
            stages.LiveDocuments = writer.CommittedSegments.Sum(static segment => segment.LiveDocCount);
        }
        finally
        {
            writer.Dispose();
        }

        stages.Disposed = Stopwatch.GetTimestamp();
        if (stages.LiveDocuments != expectedDocuments)
        {
            throw new InvalidOperationException(
                $"Benchmark completed {stages.LiveDocuments} live documents; expected {expectedDocuments}.");
        }

        s_last = stages;
        return expectedDocuments;
    }

    public static void ReportLastIteration(int documentCount)
    {
        var stages = Interlocked.Exchange(ref s_last, null);
        if (stages is null ||
            !string.Equals(Environment.GetEnvironmentVariable(StagesEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"// Async indexing stages: documents={documentCount}, ingestMs={Ms(stages.Started, stages.Ingested):F3}, " +
            $"commitMs={Ms(stages.Ingested, stages.Committed):F3}, mergeCompletionMs={Ms(stages.Committed, stages.MergesCompleted):F3}, " +
            $"disposeMs={Ms(stages.MergesCompleted, stages.Disposed):F3}, mergesStarted={stages.MergesStarted}, " +
            $"mergeRounds={stages.MergeRounds}, finalSegments={stages.FinalSegments}, liveDocuments={stages.LiveDocuments}"));
    }

    private static double Ms(long from, long to) => Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;
}

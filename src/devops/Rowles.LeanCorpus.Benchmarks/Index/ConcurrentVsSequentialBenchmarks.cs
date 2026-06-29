using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="IndexWriter.AddDocumentsConcurrent"/> and
/// <see cref="IndexWriter.AddDocumentLockFree"/> throughput against
/// sequential <see cref="IndexWriter.AddDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// The DWPT (DocumentsWriterPerThread) concurrent path partitions documents
/// across per-thread buffers that are merged under a single lock acquisition.
/// This benchmark measures whether the parallelism pays for the merge cost
/// at realistic batch sizes.
/// </para>
/// <para>
/// Run with: dotnet run --suite concurrent-write
/// </para>
/// </remarks>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class ConcurrentVsSequentialBenchmarks
{
    /// <summary>
    /// Batch sizes: small (tight loop overhead), medium (typical ingestion burst),
    /// and large (throughput ceiling).
    /// </summary>
    [Params(100, 1000, 10_000)]
    public int BatchSize { get; set; }

    // Thread count for AddDocumentLockFree DWPT pool.  4 is a realistic
    // mid-range processor count; kept separate from BatchSize since the
    // parallel-for path uses Environment.ProcessorCount internally.
    private const int DwptThreadCount = 4;

    private LeanDocument[] _documents = [];

    [GlobalSetup]
    public void Setup()
    {
        var bodies = BenchmarkData.BuildDocuments(BatchSize);
        _documents = new LeanDocument[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new TextField("body", bodies[i]));
            doc.Add(new NumericField("price", i * 1.5));
            _documents[i] = doc;
        }
    }

    /// <summary>Sequential foreach + AddDocument (baseline).</summary>
    [Benchmark(Baseline = true)]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public int Sequential_AddDocument()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-conc-seq-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        try
        {
            using var dir = new MMapDirectory(path);
            using var writer = new IndexWriter(dir, new IndexWriterConfig
            {
                MaxBufferedDocs = 10_000,
                RamBufferSizeMB = 256
            });
            foreach (var doc in _documents)
                writer.AddDocument(doc);
            writer.Commit();
            return _documents.Length;
        }
        finally
        {
            if (IODirectory.Exists(path))
                IODirectory.Delete(path, recursive: true);
        }
    }

    /// <summary>
    /// Parallel batch via <see cref="IndexWriter.AddDocumentsConcurrent"/>.
    /// Partitions across all processors and merges DWPT buffers into the main
    /// buffer under a single lock acquisition per partition.
    /// </summary>
    [Benchmark]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public int Concurrent_AddDocumentsConcurrent()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-conc-batch-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        try
        {
            using var dir = new MMapDirectory(path);
            using var writer = new IndexWriter(dir, new IndexWriterConfig
            {
                MaxBufferedDocs = 10_000,
                RamBufferSizeMB = 256
            });
            writer.AddDocumentsConcurrent(_documents);
            writer.Commit();
            return _documents.Length;
        }
        finally
        {
            if (IODirectory.Exists(path))
                IODirectory.Delete(path, recursive: true);
        }
    }

    /// <summary>
    /// Lock-free single-document addition via
    /// <see cref="IndexWriter.AddDocumentLockFree"/> with a
    /// pre-initialised DWPT pool of <see cref="DwptThreadCount"/> threads.
    /// Documents are dispatched round-robin via
    /// <see cref="System.Threading.Interlocked.Increment"/>.
    /// </summary>
    [Benchmark]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public int Concurrent_AddDocumentLockFree()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-conc-lf-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        try
        {
            using var dir = new MMapDirectory(path);
            using var writer = new IndexWriter(dir, new IndexWriterConfig
            {
                MaxBufferedDocs = 10_000,
                RamBufferSizeMB = 256
            });
            writer.InitialiseDwptPool(threadCount: DwptThreadCount);
            foreach (var doc in _documents)
                writer.AddDocumentLockFree(doc);
            writer.Commit();
            return _documents.Length;
        }
        finally
        {
            if (IODirectory.Exists(path))
                IODirectory.Delete(path, recursive: true);
        }
    }
}

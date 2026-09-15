using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares bounded <see cref="IndexWriter.AddDocumentsConcurrent"/> throughput
/// against sequential <see cref="IndexWriter.AddDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// The concurrent path uses the normal per-writer DWPT pool and detached flush
/// coordinator. This benchmark measures whether bounded producer parallelism
/// repays its coordination cost at realistic batch sizes.
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
[WarmupCount(2)]
[IterationCount(5)]
[InvocationCount(1)]
public class ConcurrentVsSequentialBenchmarks
{
    /// <summary>
    /// Batch sizes: small (tight loop overhead), medium (typical ingestion burst),
    /// and large (throughput ceiling).
    /// </summary>
    [Params(100, 1000, 10_000)]
    public int BatchSize { get; set; }

    /// <summary>DWPT pool sizes used to measure bounded producer scaling.</summary>
    [Params(1, 2, 4)]
    public int IndexingConcurrency { get; set; }

    private LeanDocument[] _documents = [];
    private readonly List<string> _iterationPaths = [];

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

    [GlobalCleanup]
    public void Cleanup() => CleanupIterationPaths();

    [IterationCleanup]
    public void CleanupIterationPaths()
    {
        foreach (var path in _iterationPaths)
            BenchmarkHelpers.DeleteDirectory(path);
        _iterationPaths.Clear();
    }

    /// <summary>Sequential foreach + AddDocument (baseline).</summary>
    [Benchmark(Baseline = true)]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public int Sequential_AddDocument()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-conc-seq-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var dir = new MMapDirectory(path);
        using var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            IndexingConcurrency = 1,
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256
        });
        foreach (var doc in _documents)
            writer.AddDocument(doc);
        writer.Commit();
        return _documents.Length;
    }

    /// <summary>
    /// Parallel batch via <see cref="IndexWriter.AddDocumentsConcurrent"/> using
    /// the configured bounded DWPT pool.
    /// </summary>
    [Benchmark]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public int Concurrent_AddDocumentsConcurrent()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-conc-batch-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var dir = new MMapDirectory(path);
        using var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            IndexingConcurrency = IndexingConcurrency,
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256
        });
        writer.AddDocumentsConcurrent(_documents);
        writer.Commit();
        return _documents.Length;
    }

}

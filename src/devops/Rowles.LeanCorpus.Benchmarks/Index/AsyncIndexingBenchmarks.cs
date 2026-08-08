using BenchmarkDotNet.Attributes;
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

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_AddDocument_Sync()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-async-sync-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var dir = new LeanMMapDirectory(path);
        using var writer = new IndexWriter(
            dir,
            new IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        foreach (var doc in _documents)
            writer.AddDocument(doc);
        writer.Commit();
        return _documents.Length;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<int> LeanCorpus_AddDocumentAsync_Sequential()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-async-seq-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var dir = new LeanMMapDirectory(path);
        using var writer = new IndexWriter(
            dir,
            new IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        foreach (var doc in _documents)
            await writer.AddDocumentAsync(doc);
        writer.Commit();
        return _documents.Length;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<int> LeanCorpus_AddDocumentsAsync_Batch()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-async-batch-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var dir = new LeanMMapDirectory(path);
        using var writer = new IndexWriter(
            dir,
            new IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        await writer.AddDocumentsAsync(_documents);
        writer.Commit();
        return _documents.Length;
    }
}

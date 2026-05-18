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
[SimpleJob]
public class AsyncIndexingBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private LeanDocument[] _documents = [];

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

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_AddDocument_Sync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leancorpus-bench-async-sync-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        try
        {
            using var dir = new LeanMMapDirectory(path);
            using var writer = new IndexWriter(
                dir,
                new IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
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

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<int> LeanCorpus_AddDocumentAsync_Sequential()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leancorpus-bench-async-seq-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        try
        {
            using var dir = new LeanMMapDirectory(path);
            using var writer = new IndexWriter(
                dir,
                new IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
            foreach (var doc in _documents)
                await writer.AddDocumentAsync(doc);
            writer.Commit();
            return _documents.Length;
        }
        finally
        {
            if (IODirectory.Exists(path))
                IODirectory.Delete(path, recursive: true);
        }
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<int> LeanCorpus_AddDocumentsAsync_Batch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leancorpus-bench-async-batch-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        try
        {
            using var dir = new LeanMMapDirectory(path);
            using var writer = new IndexWriter(
                dir,
                new IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
            await writer.AddDocumentsAsync(_documents);
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

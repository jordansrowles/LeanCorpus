using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LeanNumericField = Rowles.LeanCorpus.Document.Fields.NumericField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="SegmentFlusher"/> flush latency via <see cref="IndexWriter.Commit"/>
/// across different field configurations and batch sizes. Each iteration builds
/// a fresh index through four DWPT producers so the flush-limit sweep admits
/// genuinely concurrent physical work.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[WarmupCount(2)]
[IterationCount(5)]
[InvocationCount(1)]
public class FlushBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(100, 1_000, 10_000);

    [ParamsSource(nameof(DocCounts))]
    public int DocsPerFlush { get; set; }

    /// <summary>Physical detached-flush concurrency limits to compare.</summary>
    [Params(1, 2, 4)]
    public int MaxConcurrentFlushes { get; set; }

    private string[] _documents = [];
    private float[][] _vectors = [];
    private readonly List<string> _createdPaths = [];

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocsPerFlush);
        _vectors = BenchmarkVectorData.Get(DocsPerFlush, 64).Records
            .Select(static record => record.Vector).ToArray();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        foreach (var path in _createdPaths)
            BenchmarkHelpers.DeleteDirectory(path);
        _createdPaths.Clear();
    }

    [Benchmark(Baseline = true, Description = "Flush text-only docs")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Flush_TextOnly()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-flush-bench-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _createdPaths.Add(path);

        using (var dir = new MMapDirectory(path))
        using (var writer = new IndexWriter(dir, new IndexWriterConfig
               {
                   MaxBufferedDocs = DocsPerFlush,
                   MaxConcurrentFlushes = MaxConcurrentFlushes,
                   IndexingConcurrency = 4,
                   RamBufferSizeMB = 64
               }))
        {
            var documents = new LeanDocument[_documents.Length];
            for (int i = 0; i < _documents.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id",
                    i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new LeanTextField("body", _documents[i]));
                documents[i] = doc;
            }
            writer.AddDocumentsConcurrent(documents);
            writer.Commit(); // triggers flush
            return _documents.Length;
        }
    }

    [Benchmark(Description = "Flush mixed-field docs")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Flush_MixedFields()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-flush-bench-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _createdPaths.Add(path);

        using (var dir = new MMapDirectory(path))
        using (var writer = new IndexWriter(dir, new IndexWriterConfig
               {
                   MaxBufferedDocs = DocsPerFlush,
                   MaxConcurrentFlushes = MaxConcurrentFlushes,
                   IndexingConcurrency = 4,
                   RamBufferSizeMB = 64
               }))
        {
            var documents = new LeanDocument[_documents.Length];
            for (int i = 0; i < _documents.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id",
                    i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new LeanTextField("body", _documents[i]));
                doc.Add(new LeanNumericField("price", i * 1.5));
                doc.Add(new LeanStringField("tag", $"cat{i % 10}"));
                documents[i] = doc;
            }
            writer.AddDocumentsConcurrent(documents);
            writer.Commit();
            return _documents.Length;
        }
    }

    [Benchmark(Description = "Flush docs with vectors")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Flush_WithVectors()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-flush-bench-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _createdPaths.Add(path);

        using (var dir = new MMapDirectory(path))
        using (var writer = new IndexWriter(dir, new IndexWriterConfig
               {
                   MaxBufferedDocs = DocsPerFlush,
                   MaxConcurrentFlushes = MaxConcurrentFlushes,
                   IndexingConcurrency = 4,
                   RamBufferSizeMB = 64,
                   BuildHnswOnFlush = true,
                   HnswBuildConfig = new Rowles.LeanCorpus.Codecs.Hnsw.HnswBuildConfig
                       { M = 8, M0 = 16, EfConstruction = 50 },
                   HnswSeed = 1L,
               }))
        {
            var documents = new LeanDocument[_documents.Length];
            for (int i = 0; i < _documents.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id",
                    i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new LeanTextField("body", _documents[i]));
                doc.Add(new Rowles.LeanCorpus.Document.Fields.VectorField("emb",
                    new ReadOnlyMemory<float>(_vectors[i])));
                documents[i] = doc;
            }
            writer.AddDocumentsConcurrent(documents);
            writer.Commit();
            return _documents.Length;
        }
    }

    [Benchmark(Description = "Flush docs with term vectors")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Flush_WithTermVectors()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-flush-bench-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _createdPaths.Add(path);

        using (var dir = new MMapDirectory(path))
        using (var writer = new IndexWriter(dir, new IndexWriterConfig
               {
                   MaxBufferedDocs = DocsPerFlush,
                   MaxConcurrentFlushes = MaxConcurrentFlushes,
                   IndexingConcurrency = 4,
                   RamBufferSizeMB = 64,
                   StoreTermVectors = true,
               }))
        {
            var documents = new LeanDocument[_documents.Length];
            for (int i = 0; i < _documents.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id",
                    i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new LeanTextField("body", _documents[i]));
                documents[i] = doc;
            }
            writer.AddDocumentsConcurrent(documents);
            writer.Commit();
            return _documents.Length;
        }
    }
}

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
/// across different field configurations and batch sizes.
/// Each iteration builds a fresh index and measures the cumulative flush cost.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class FlushBenchmarks
{
    [Params(100, 1_000, 10_000)]
    public int DocsPerFlush { get; set; }

    private string[] _documents = [];

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocsPerFlush);
    }

    [Benchmark(Baseline = true, Description = "Flush text-only docs")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Flush_TextOnly()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-flush-bench-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);

        try
        {
            var dir = new MMapDirectory(path);
            using var writer = new IndexWriter(dir, new IndexWriterConfig
            {
                MaxBufferedDocs = DocsPerFlush,
                RamBufferSizeMB = 64
            });
            for (int i = 0; i < _documents.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id",
                    i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new LeanTextField("body", _documents[i]));
                writer.AddDocument(doc);
            }
            writer.Commit(); // triggers flush
            return _documents.Length;
        }
        finally
        {
            if (IODirectory.Exists(path))
                IODirectory.Delete(path, recursive: true);
        }
    }

    [Benchmark(Description = "Flush mixed-field docs")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Flush_MixedFields()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-flush-bench-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);

        try
        {
            var dir = new MMapDirectory(path);
            using var writer = new IndexWriter(dir, new IndexWriterConfig
            {
                MaxBufferedDocs = DocsPerFlush,
                RamBufferSizeMB = 64
            });
            for (int i = 0; i < _documents.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id",
                    i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new LeanTextField("body", _documents[i]));
                doc.Add(new LeanNumericField("price", i * 1.5));
                doc.Add(new LeanStringField("tag", $"cat{i % 10}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
            return _documents.Length;
        }
        finally
        {
            if (IODirectory.Exists(path))
                IODirectory.Delete(path, recursive: true);
        }
    }

    [Benchmark(Description = "Flush docs with vectors")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Flush_WithVectors()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-flush-bench-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);

        try
        {
            var dir = new MMapDirectory(path);
            var rnd = new Random(7);
            using var writer = new IndexWriter(dir, new IndexWriterConfig
            {
                MaxBufferedDocs = DocsPerFlush,
                RamBufferSizeMB = 64,
                BuildHnswOnFlush = true,
                HnswBuildConfig = new Rowles.LeanCorpus.Codecs.Hnsw.HnswBuildConfig
                    { M = 8, M0 = 16, EfConstruction = 50 },
                HnswSeed = 1L,
            });
            for (int i = 0; i < _documents.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id",
                    i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new LeanTextField("body", _documents[i]));
                var vec = new float[64];
                for (int d = 0; d < 64; d++)
                    vec[d] = (float)(rnd.NextDouble() * 2 - 1);
                doc.Add(new Rowles.LeanCorpus.Document.Fields.VectorField("emb",
                    new ReadOnlyMemory<float>(vec)));
                writer.AddDocument(doc);
            }
            writer.Commit();
            return _documents.Length;
        }
        finally
        {
            if (IODirectory.Exists(path))
                IODirectory.Delete(path, recursive: true);
        }
    }

    [Benchmark(Description = "Flush docs with term vectors")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Flush_WithTermVectors()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-flush-bench-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);

        try
        {
            var dir = new MMapDirectory(path);
            using var writer = new IndexWriter(dir, new IndexWriterConfig
            {
                MaxBufferedDocs = DocsPerFlush,
                RamBufferSizeMB = 64,
                StoreTermVectors = true,
            });
            for (int i = 0; i < _documents.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id",
                    i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new LeanTextField("body", _documents[i]));
                writer.AddDocument(doc);
            }
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
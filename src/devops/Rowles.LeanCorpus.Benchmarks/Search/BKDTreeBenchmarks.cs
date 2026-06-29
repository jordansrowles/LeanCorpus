using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanNumericField = Rowles.LeanCorpus.Document.Fields.NumericField;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures BKD-backed range query throughput via <see cref="IndexSearcher"/>
/// at varying point counts and range selectivities.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class BKDTreeBenchmarks
{
    [Params(10_000, 100_000)]
    public int PointCount { get; set; }

    /// <summary>Range width as a fraction of the full 0–10000 value space.</summary>
    [Params(0.01, 0.1, 0.5)]
    public double RangeWidth { get; set; } = 0.1;

    private string _indexPath = string.Empty;
    private MMapDirectory? _directory;
    private LeanIndexSearcher? _searcher;

    [GlobalSetup]
    public void Setup()
    {
        _indexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-bkd-bench-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_indexPath);
        _directory = new MMapDirectory(_indexPath);

        using var writer = new IndexWriter(_directory, new IndexWriterConfig
        {
            MaxBufferedDocs = PointCount,
            RamBufferSizeMB = 256
        });
        var rnd = new Random(42);
        for (int i = 0; i < PointCount; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id",
                i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanNumericField("value", rnd.NextDouble() * 10_000));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _searcher = new LeanIndexSearcher(_directory);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _searcher?.Dispose();
        if (!string.IsNullOrWhiteSpace(_indexPath) && IODirectory.Exists(_indexPath))
            IODirectory.Delete(_indexPath, recursive: true);
    }

    [Benchmark(Baseline = true, Description = "BKD range 1%")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_BKD_Range1Percent()
    {
        double span = 100.0;
        double min = 2500.0;
        return _searcher!.Search(
            new RangeQuery("value", min, min + span), 100).TotalHits;
    }

    [Benchmark(Description = "BKD range 10%")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_BKD_Range10Percent()
    {
        double span = 1000.0;
        double min = 2500.0;
        return _searcher!.Search(
            new RangeQuery("value", min, min + span), 100).TotalHits;
    }

    [Benchmark(Description = "BKD range 50%")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_BKD_Range50Percent()
    {
        double span = 5000.0;
        double min = 2500.0;
        return _searcher!.Search(
            new RangeQuery("value", min, min + span), 100).TotalHits;
    }
}
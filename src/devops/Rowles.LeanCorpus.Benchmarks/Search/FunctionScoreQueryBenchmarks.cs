using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Search.Scoring;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanNumericField = Rowles.LeanCorpus.Document.Fields.NumericField;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="FunctionScoreQuery"/> latency across all <see cref="ScoreMode"/> values.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class FunctionScoreQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("Multiply", "Replace", "Sum", "Max")]
    public string Mode { get; set; } = "Multiply";

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;

    [GlobalSetup]
    public void Setup()
    {
        var docs = BenchmarkData.BuildDocumentsWithPrices(DocumentCount);
        BuildLeanIndex(docs);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_BaseTermQuery()
        => _leanSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_FunctionScoreQuery()
    {
        var mode = Mode switch
        {
            "Multiply" => ScoreMode.Multiply,
            "Replace"  => ScoreMode.Replace,
            "Sum"      => ScoreMode.Sum,
            "Max"      => ScoreMode.Max,
            _          => ScoreMode.Multiply
        };
        var q = new FunctionScoreQuery(new TermQuery("body", "government"), "price", mode);
        return _leanSearcher!.Search(q, TopN).TotalHits;
    }

    private void BuildLeanIndex((string Body, double Price)[] docs)
    {
        _leanIndexPath = Path.Combine(Path.GetTempPath(), $"leancorpus-bench-funcscore-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        for (int i = 0; i < docs.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", docs[i].Body));
            doc.Add(new LeanNumericField("price", docs[i].Price));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _leanSearcher = new LeanIndexSearcher(_leanDirectory);
    }
}

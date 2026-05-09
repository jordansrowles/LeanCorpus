using BenchmarkDotNet.Attributes;
using Rowles.LeanLucene.Diagnostics;
using Rowles.LeanLucene.Store;
using LeanDocument = Rowles.LeanLucene.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanLucene.Search.Searcher.IndexSearcher;
using LeanIndexSearcherConfig = Rowles.LeanLucene.Search.Searcher.IndexSearcherConfig;
using LeanMMapDirectory = Rowles.LeanLucene.Store.MMapDirectory;
using LeanStringField = Rowles.LeanLucene.Document.Fields.StringField;
using LeanTextField = Rowles.LeanLucene.Document.Fields.TextField;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Measures overhead of SlowQueryLog and SearchAnalytics hooks during search.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[SimpleJob]
public class DiagnosticsBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string _indexPath = string.Empty;
    private LeanMMapDirectory? _directory;
    private LeanIndexSearcher? _noHooksSearcher;
    private LeanIndexSearcher? _slowLogSearcher;
    private LeanIndexSearcher? _analyticsSearcher;
    private LeanIndexSearcher? _allHooksSearcher;
    private SlowQueryLog? _slowLog;

    [GlobalSetup]
    public void Setup()
    {
        _indexPath = Path.Combine(Path.GetTempPath(), $"leanlucene-bench-diag-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_indexPath);

        _directory = new LeanMMapDirectory(_indexPath);
        var docs = BenchmarkData.BuildDocuments(DocumentCount);

        using (var writer = new IndexWriter(_directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256
        }))
        {
            for (int i = 0; i < docs.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new LeanTextField("body", docs[i]));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        // High threshold so nothing is actually logged — measures pure hook overhead
        _slowLog = new SlowQueryLog(999_999, TextWriter.Null);

        _noHooksSearcher = new LeanIndexSearcher(_directory);
        _slowLogSearcher = new LeanIndexSearcher(_directory, new LeanIndexSearcherConfig
        {
            SlowQueryLog = _slowLog
        });
        _analyticsSearcher = new LeanIndexSearcher(_directory, new LeanIndexSearcherConfig
        {
            SearchAnalytics = new SearchAnalytics(1000)
        });
        _allHooksSearcher = new LeanIndexSearcher(_directory, new LeanIndexSearcherConfig
        {
            SlowQueryLog = _slowLog,
            SearchAnalytics = new SearchAnalytics(1000)
        });
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _noHooksSearcher?.Dispose();
        _slowLogSearcher?.Dispose();
        _analyticsSearcher?.Dispose();
        _allHooksSearcher?.Dispose();
        _slowLog?.Dispose();

        if (!string.IsNullOrWhiteSpace(_indexPath) && Directory.Exists(_indexPath))
            Directory.Delete(_indexPath, recursive: true);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_Search_NoHooks()
    {
        var topDocs = _noHooksSearcher!.Search(new TermQuery("body", "search"), TopN);
        return topDocs.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_Search_SlowQueryLog()
    {
        var topDocs = _slowLogSearcher!.Search(new TermQuery("body", "search"), TopN);
        return topDocs.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_Search_Analytics()
    {
        var topDocs = _analyticsSearcher!.Search(new TermQuery("body", "search"), TopN);
        return topDocs.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_Search_AllHooks()
    {
        var topDocs = _allHooksSearcher!.Search(new TermQuery("body", "search"), TopN);
        return topDocs.TotalHits;
    }
}

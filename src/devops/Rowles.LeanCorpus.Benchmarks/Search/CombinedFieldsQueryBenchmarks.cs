using BenchmarkDotNet.Attributes;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="CombinedFieldsQuery"/> multi-field BM25F throughput
/// against an equivalent <see cref="BooleanQuery"/> across title and body fields.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class CombinedFieldsQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params(1, 2)]
    public int MinimumShouldMatch { get; set; } = 1;

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;

    [GlobalSetup]
    public void Setup()
    {
        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        BuildLeanIndex(documents);
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
    public int LeanCorpus_CombinedFieldsQuery()
    {
        var q = new CombinedFieldsQuery(
            ["title", "body"],
            ["government", "market"],
            minimumShouldMatch: MinimumShouldMatch,
            fieldWeights: new Dictionary<string, float> { ["title"] = 2.0f, ["body"] = 1.0f });
        return _leanSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_BooleanQuery_MultiField()
    {
        var builder = new Rowles.LeanCorpus.Search.Queries.BooleanQuery.Builder();
        builder.Add(new TermQuery("title", "government"), Rowles.LeanCorpus.Search.Occur.Should);
        builder.Add(new TermQuery("title", "market"), Rowles.LeanCorpus.Search.Occur.Should);
        builder.Add(new TermQuery("body", "government"), Rowles.LeanCorpus.Search.Occur.Should);
        builder.Add(new TermQuery("body", "market"), Rowles.LeanCorpus.Search.Occur.Should);
        return _leanSearcher!.Search(builder.Build(), TopN).TotalHits;
    }

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(Path.GetTempPath(), $"leancorpus-bench-combined-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        for (int i = 0; i < documents.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            // Extract a title from the first sentence
            var body = documents[i];
            var dot = body.IndexOf('.', StringComparison.Ordinal);
            var title = dot > 0 && dot <= 120 ? body[..dot] : body[..Math.Min(80, body.Length)];
            doc.Add(new LeanTextField("title", title));
            doc.Add(new LeanTextField("body", body));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _leanSearcher = new LeanIndexSearcher(_leanDirectory);
    }
}

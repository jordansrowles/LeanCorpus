using BenchmarkDotNet.Attributes;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="TermInSetQuery"/> throughput against an equivalent
/// multi-clause <see cref="BooleanQuery"/> at different set sizes.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class TermInSetQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    /// <summary>Number of terms in the set.</summary>
    [Params(5, 20, 100)]
    public int SetSize { get; set; } = 20;

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;
    private string[] _terms = [];

    private static readonly string[] Vocabulary =
    [
        "government", "market", "people", "national", "said", "company", "year", "new",
        "president", "state", "time", "reported", "million", "political", "economic",
        "country", "official", "party", "council", "program", "project", "agency",
        "office", "minister", "policy", "leader", "region", "sector", "report",
        "financial", "international", "united", "federal", "local", "public",
        "private", "major", "general", "central", "north", "south", "east", "west",
        "director", "head", "chief", "secretary", "chairman", "governor", "senator",
        "assembly", "parliament", "congress", "committee", "department", "bureau",
        "court", "law", "bill", "vote", "election", "campaign", "budget", "tax",
        "trade", "export", "import", "bank", "fund", "investment", "stock", "bond",
        "price", "rate", "growth", "inflation", "employment", "output", "demand",
        "supply", "service", "product", "industry", "energy", "power", "oil", "gas",
        "water", "land", "city", "town", "village", "district", "province", "region",
        "military", "force", "army", "navy", "troops", "border", "security", "peace"
    ];

    [GlobalSetup]
    public void Setup()
    {
        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        _terms = Vocabulary.Take(SetSize).ToArray();
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
    public int LeanCorpus_TermInSetQuery()
        => _leanSearcher!.Search(new TermInSetQuery("body", _terms), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_BooleanQuery_Should()
    {
        var builder = new Rowles.LeanCorpus.Search.Queries.BooleanQuery.Builder();
        foreach (var term in _terms)
            builder.Add(new TermQuery("body", term), Rowles.LeanCorpus.Search.Occur.Should);
        return _leanSearcher!.Search(builder.Build(), TopN).TotalHits;
    }

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(Path.GetTempPath(), $"leancorpus-bench-terminset-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        for (int i = 0; i < documents.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", documents[i]));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _leanSearcher = new LeanIndexSearcher(_leanDirectory);
    }
}

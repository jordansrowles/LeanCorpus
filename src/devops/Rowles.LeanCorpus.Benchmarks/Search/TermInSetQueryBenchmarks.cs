using BenchmarkDotNet.Attributes;
using Lucene.Net.Search;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneTermQuery = Lucene.Net.Search.TermQuery;
using LuceneTerm = Lucene.Net.Index.Term;
using TermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;

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
public class TermInSetQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    /// <summary>Number of terms in the set.</summary>
    [Params(5, 20, 100)]
    public int SetSize { get; set; } = 20;

    private LeanIndexSearcher? _leanSearcher;
    private LuceneIndexSearcher? _luceneSearcher;
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
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _leanSearcher = SharedStandardIndex.LeanSearcher;
        _luceneSearcher = SharedStandardIndex.LuceneSearcher;
        _terms = Vocabulary.Take(SetSize).ToArray();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Resources are owned by SharedStandardIndex; do not dispose.
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

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_BooleanQuery_Should()
    {
        var bq = new Lucene.Net.Search.BooleanQuery();
        foreach (var term in _terms)
            bq.Add(new LuceneTermQuery(new LuceneTerm("body", term)), Occur.SHOULD);
        return _luceneSearcher!.Search(bq, TopN).TotalHits;
    }

}

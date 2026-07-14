using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LuceneDocument = Lucene.Net.Documents.Document;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneDirectoryReader = Lucene.Net.Index.DirectoryReader;
using LuceneRAMDirectory = Lucene.Net.Store.RAMDirectory;
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

    private LeanIndexSearcher? _leanSearcher;
    private LuceneRAMDirectory? _luceneDirectory;
    private LuceneDirectoryReader? _luceneReader;
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
        _terms = Vocabulary.Take(SetSize).ToArray();
        EnsureLuceneIndex();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Lean resources owned by SharedStandardIndex; do not dispose.
        _luceneReader?.Dispose();
        _luceneDirectory?.Dispose();
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

    private void EnsureLuceneIndex()
    {
        if (_luceneSearcher is not null)
            return;

        _luceneDirectory = new LuceneRAMDirectory();
        var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser));
        var documents = SharedStandardIndex.Documents;
        for (int i = 0; i < documents.Length; i++)
        {
            var doc = new LuceneDocument();
            doc.Add(new LuceneStringField("id",
                i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneTextField("body", documents[i],
                Lucene.Net.Documents.Field.Store.NO));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _luceneReader = LuceneDirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new LuceneIndexSearcher(_luceneReader);
    }

}

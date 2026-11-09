using BenchmarkDotNet.Attributes;
using Lucene.Net.Search;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneTermQuery = Lucene.Net.Search.TermQuery;
using LuceneTerm = Lucene.Net.Index.Term;
using TermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="TermInSetQuery"/> and multi-clause Boolean OR
/// implementations at different set sizes. The methods are distinct algorithms.
/// Every term in the set occurs in the generated corpus, so <see cref="SetSize"/> is also the
/// number of live terms (ADR045).
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

    /// <summary>Preferred terms, in order. Terms absent from the corpus are skipped.</summary>
    internal static readonly string[] Vocabulary =
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
        _terms = SelectLiveTerms(BenchmarkData.BuildDocuments(DocumentCount), SetSize);
        ValidateLiveTerms(_terms, SetSize,
            term => _leanSearcher.Search(new TermQuery("body", term), 1).TotalHits,
            term => _luceneSearcher.Search(new LuceneTermQuery(new LuceneTerm("body", term)), 1).TotalHits);

        var termSetResults = _leanSearcher.Search(new TermInSetQuery("body", _terms), TopN);
        var leanBooleanResults = _leanSearcher.Search(BuildLeanBooleanQuery(), TopN);
        var luceneBooleanResults = _luceneSearcher.Search(BuildLuceneBooleanQuery(), TopN);
        BenchmarkFixtureEvidence.RequireWork("terminset", $"SetSize={SetSize}", BenchmarkFixtureEvidence.LeanCorpusEngine, termSetResults.TotalHits);
        BenchmarkFixtureEvidence.RequireWork("terminset", $"SetSize={SetSize}", BenchmarkFixtureEvidence.LuceneNetEngine, luceneBooleanResults.TotalHits);
        if (termSetResults.TotalHits != leanBooleanResults.TotalHits
            || termSetResults.TotalHits != luceneBooleanResults.TotalHits)
        {
            throw new InvalidOperationException(
                $"TermInSet and Boolean OR total hits differ for {SetSize} terms: " +
                $"TermInSet={termSetResults.TotalHits}, LeanCorpus Boolean={leanBooleanResults.TotalHits}, " +
                $"Lucene.NET Boolean={luceneBooleanResults.TotalHits}.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Resources are owned by SharedStandardIndex; do not dispose.
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_TermInSetQuery()
        => _leanSearcher!.Search(new TermInSetQuery("body", _terms), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_BooleanQuery_Should()
        => _leanSearcher!.Search(BuildLeanBooleanQuery(), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_BooleanQuery_Should()
        => _luceneSearcher!.Search(BuildLuceneBooleanQuery(), TopN).TotalHits;

    /// <summary>
    /// Selects <paramref name="setSize"/> terms that occur in the generated bodies: the
    /// <see cref="Vocabulary"/> terms that occur, in order, then other lower-case ASCII body tokens
    /// in ordinal order. Selection uses whitespace tokens, independently of both engines.
    /// </summary>
    internal static string[] SelectLiveTerms(IReadOnlyList<string> documents, int setSize)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var corpusTerms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            foreach (var token in document.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string term = token.ToLowerInvariant();
                if (term.Length > 2 && term.All(char.IsAsciiLetterLower))
                    corpusTerms.Add(term);
            }
        }

        var selected = Vocabulary.Distinct(StringComparer.Ordinal).Where(corpusTerms.Contains).Take(setSize).ToList();
        if (selected.Count < setSize)
        {
            var chosen = new HashSet<string>(selected, StringComparer.Ordinal);
            selected.AddRange(corpusTerms.Where(term => !chosen.Contains(term) && !StopWords.Contains(term))
                .Order(StringComparer.Ordinal)
                .Take(setSize - selected.Count));
        }

        if (selected.Count < setSize)
            throw new InvalidOperationException($"The corpus has only {selected.Count} usable terms for a set of {setSize}.");
        return selected.ToArray();
    }

    /// <summary>Fails setup unless every term matches at least one document on both engines.</summary>
    internal static void ValidateLiveTerms(IReadOnlyList<string> terms, int setSize, Func<string, int> leanHits, Func<string, int> luceneHits)
    {
        ArgumentNullException.ThrowIfNull(terms);
        int leanLive = terms.Count(term => leanHits(term) > 0);
        int luceneLive = terms.Count(term => luceneHits(term) > 0);
        Console.WriteLine($"Benchmark preflight terminset SetSize={setSize}: terms={terms.Count}, live LeanCorpus={leanLive}, live Lucene.NET={luceneLive}");
        if (terms.Count != setSize || leanLive != setSize || luceneLive != setSize)
            throw new InvalidOperationException(
                $"Term set of size {setSize} has {terms.Count} terms, {leanLive} live on LeanCorpus and {luceneLive} live on Lucene.NET.");
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "and", "are", "but", "for", "into", "not", "such", "that", "the", "their", "then", "there",
        "these", "they", "this", "was", "will", "with"
    };

    private Rowles.LeanCorpus.Search.Queries.BooleanQuery BuildLeanBooleanQuery()
    {
        var builder = new Rowles.LeanCorpus.Search.Queries.BooleanQuery.Builder();
        foreach (var term in _terms)
            builder.Add(new TermQuery("body", term), Rowles.LeanCorpus.Search.Occur.Should);
        return builder.Build();
    }

    private Lucene.Net.Search.BooleanQuery BuildLuceneBooleanQuery()
    {
        var query = new Lucene.Net.Search.BooleanQuery();
        foreach (var term in _terms)
            query.Add(new LuceneTermQuery(new LuceneTerm("body", term)), Occur.SHOULD);
        return query;
    }

}

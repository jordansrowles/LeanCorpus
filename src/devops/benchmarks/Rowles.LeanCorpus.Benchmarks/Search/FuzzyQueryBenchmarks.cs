using BenchmarkDotNet.Attributes;
using Lucene.Net.Index;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanIndexSearcherConfig = Rowles.LeanCorpus.Search.Searcher.IndexSearcherConfig;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares uncached fuzzy-query results under matched Levenshtein settings.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class FuzzyQueryBenchmarks
{
    private const int TopN = 25;
    private const int MaxExpansions = 50;
    private const string FieldPrefix = "body\0";

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params(
        "exact-edit0-common",
        "short-edit1-common",
        "medium-edit1-common",
        "medium-edit2-common",
        "long-edit1-common",
        "near-miss-transposition-edit1",
        "nohit-edit2")]
    public string Scenario { get; set; } = "medium-edit1-common";

    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;
    private Lucene.Net.Search.IndexSearcher? _luceneSearcher;
    private Rowles.LeanCorpus.Search.Queries.FuzzyQuery? _leanQuery;
    private Lucene.Net.Search.FuzzyQuery? _luceneQuery;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _leanDirectory = SharedStandardIndex.CreateDirectory();
        _leanSearcher = new LeanIndexSearcher(_leanDirectory, new LeanIndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = false
        });
        _luceneSearcher = SharedStandardIndex.LuceneSearcher;

        var scenario = ResolveScenario(Scenario);
        _leanQuery = new Rowles.LeanCorpus.Search.Queries.FuzzyQuery(
            "body", scenario.QueryTerm, scenario.MaxEdits, MaxExpansions);
        _luceneQuery = new Lucene.Net.Search.FuzzyQuery(
            new Term("body", scenario.QueryTerm),
            maxEdits: scenario.MaxEdits,
            prefixLength: 0,
            maxExpansions: MaxExpansions,
            transpositions: false);

        var leanTerms = _leanSearcher.GetSegmentReaders()
            .SelectMany(reader => reader.GetFuzzyMatches(FieldPrefix, scenario.QueryTerm.AsSpan(), scenario.MaxEdits,
                MaxExpansions, useCache: false))
            .Select(static match => match.Term[FieldPrefix.Length..])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var luceneTerms = GetLuceneFuzzyExpansionTerms(_luceneSearcher, _luceneQuery);
        var leanResults = _leanSearcher.Search(_leanQuery, DocumentCount);
        var luceneResults = _luceneSearcher.Search(_luceneQuery, DocumentCount);
        var leanHits = leanResults.TotalHits;
        var luceneHits = luceneResults.TotalHits;
        var leanDocIds = leanResults.ScoreDocs.Select(static hit => hit.DocId).Order().ToArray();
        var luceneDocIds = luceneResults.ScoreDocs.Select(static hit => hit.Doc).Order().ToArray();

        var requiresNoMatch = Scenario is "near-miss-transposition-edit1" or "nohit-edit2";
        if (requiresNoMatch && (leanTerms.Length != 0 || luceneTerms.Length != 0 || leanHits != 0 || luceneHits != 0))
            throw new InvalidOperationException(
                $"Fuzzy no-match preflight '{Scenario}' returned LeanCorpus {leanTerms.Length} expansions/{leanHits} hits and Lucene.NET {luceneTerms.Length} expansions/{luceneHits} hits; expected zero for both engines.");

        if (!requiresNoMatch && (leanTerms.Length == 0 || luceneTerms.Length == 0 || leanHits == 0 || luceneHits == 0))
            throw new InvalidOperationException(
                $"Fuzzy positive preflight '{Scenario}' returned LeanCorpus {leanTerms.Length} expansions/{leanHits} hits and Lucene.NET {luceneTerms.Length} expansions/{luceneHits} hits; expected positive expansions and hits for both engines.");

        if (!leanTerms.SequenceEqual(luceneTerms, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Fuzzy preflight '{Scenario}' expanded different terms: LeanCorpus only [{FormatDifference(leanTerms.Except(luceneTerms, StringComparer.Ordinal))}], Lucene.NET only [{FormatDifference(luceneTerms.Except(leanTerms, StringComparer.Ordinal))}].");

        if (!leanDocIds.SequenceEqual(luceneDocIds))
        {
            var leanOnlyDocIds = leanDocIds.Except(luceneDocIds)
                .Select(static id => id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var luceneOnlyDocIds = luceneDocIds.Except(leanDocIds)
                .Select(static id => id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            throw new InvalidOperationException(
                $"Fuzzy preflight '{Scenario}' returned {leanHits} LeanCorpus hits and {luceneHits} Lucene.NET hits with different logical document IDs: LeanCorpus only [{FormatDifference(leanOnlyDocIds)}], Lucene.NET only [{FormatDifference(luceneOnlyDocIds)}].");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        _leanDirectory?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_FuzzyQuery()
        => _leanSearcher!.Search(_leanQuery!, TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_FuzzyQuery()
        => _luceneSearcher!.Search(_luceneQuery!, TopN).TotalHits;

    private static string[] GetLuceneFuzzyExpansionTerms(
        Lucene.Net.Search.IndexSearcher searcher,
        Lucene.Net.Search.FuzzyQuery query)
    {
        var rewritten = searcher.Rewrite(query);
        if (rewritten is Lucene.Net.Search.TermQuery singleTermQuery)
            return [singleTermQuery.Term.Text];

        if (rewritten is not Lucene.Net.Search.BooleanQuery booleanQuery)
            throw new InvalidOperationException(
                $"Lucene.NET fuzzy query rewrote to {rewritten.GetType().Name}; expected a TermQuery or BooleanQuery containing its term expansions.");

        var terms = new List<string>(booleanQuery.Clauses.Count);
        foreach (var clause in booleanQuery.Clauses)
        {
            if (clause.Query is not Lucene.Net.Search.TermQuery termQuery)
                throw new InvalidOperationException(
                    $"Lucene.NET fuzzy query expansion contained {clause.Query.GetType().Name}; expected a term query.");
            terms.Add(termQuery.Term.Text);
        }

        return terms.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static string FormatDifference(IEnumerable<string> values)
    {
        var items = values.Take(10).ToArray();
        return items.Length == 0 ? "<none>" : string.Join(", ", items);
    }

    private static (string QueryTerm, int MaxEdits) ResolveScenario(string scenario) => scenario switch
    {
        "exact-edit0-common" => ("government", 0),
        "short-edit1-common" => ("marke", 1),
        "medium-edit1-common" => ("goverment", 1),
        "medium-edit2-common" => ("presdnt", 2),
        "long-edit1-common" => ("econmic", 1),
        "near-miss-transposition-edit1" => ("governemnt", 1),
        "nohit-edit2" => ("zzzznomatch", 2),
        _ => throw new InvalidOperationException($"Unknown fuzzy benchmark scenario '{scenario}'.")
    };
}

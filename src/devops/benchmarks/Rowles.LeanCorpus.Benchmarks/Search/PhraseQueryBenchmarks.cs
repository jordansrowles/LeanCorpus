using BenchmarkDotNet.Attributes;
using Lucene.Net.Index;
using Lucene.Net.Search;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures PhraseQuery performance: exact and slop phrase matching.
/// Compares LeanCorpus vs Lucene.NET (Lifti lacks first-class phrase support).
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class PhraseQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("ExactTwoWord", "ExactThreeWord", "SlopTwoWord")]
    public string PhraseType { get; set; } = "ExactTwoWord";

    private LeanIndexSearcher? _leanSearcher;

    private static readonly string[] TwoWordTerms = ["new", "york"];
    private static readonly int[] TwoWordPositions = [0, 1];
    private static readonly string[] ThreeWordTerms = ["new", "york", "stock"];
    private static readonly int[] ThreeWordPositions = [0, 1, 2];
    private static readonly string[] SlopTwoWordTerms = ["said", "government"];

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _leanSearcher = SharedStandardIndex.LeanSearcher;
        ValidatePhraseParity();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // All resources are owned by SharedStandardIndex; do not dispose.
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_PhraseQuery()
    {
        return _leanSearcher!.Search(BuildLeanPhraseQuery(), TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_PhraseQueryConstruction()
    {
        return BuildLeanPhraseQuery().GetHashCode();
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_PhraseQuery()
    {
        return SharedStandardIndex.LuceneSearcher.Search(BuildLucenePhraseQuery(), TopN).TotalHits;
    }

    private Rowles.LeanCorpus.Search.Queries.PhraseQuery BuildLeanPhraseQuery()
    {
        string[] terms = PhraseType switch
        {
            "ExactTwoWord" => TwoWordTerms,
            "ExactThreeWord" => ThreeWordTerms,
            "SlopTwoWord" => SlopTwoWordTerms,
            _ => throw new InvalidOperationException($"Unknown phrase type '{PhraseType}'.")
        };
        int[] positions = PhraseType == "ExactThreeWord" ? ThreeWordPositions : TwoWordPositions;
        int slop = PhraseType == "SlopTwoWord" ? 2 : 0;
        return new Rowles.LeanCorpus.Search.Queries.PhraseQuery("body", terms, positions, slop);
    }

    private Lucene.Net.Search.PhraseQuery BuildLucenePhraseQuery()
    {
        string[] terms = PhraseType switch
        {
            "ExactTwoWord" => TwoWordTerms,
            "ExactThreeWord" => ThreeWordTerms,
            "SlopTwoWord" => SlopTwoWordTerms,
            _ => throw new InvalidOperationException($"Unknown phrase type '{PhraseType}'.")
        };
        var query = new Lucene.Net.Search.PhraseQuery { Slop = PhraseType == "SlopTwoWord" ? 2 : 0 };
        foreach (string term in terms)
            query.Add(new Term("body", term));
        return query;
    }

    private void ValidatePhraseParity()
    {
        var leanResults = _leanSearcher!.Search(BuildLeanPhraseQuery(), DocumentCount);
        var luceneResults = SharedStandardIndex.LuceneSearcher.Search(BuildLucenePhraseQuery(), DocumentCount);
        int[] leanDocumentIds = leanResults.ScoreDocs.Select(static hit => hit.DocId).Order().ToArray();
        int[] luceneDocumentIds = luceneResults.ScoreDocs.Select(static hit => hit.Doc).Order().ToArray();
        if (leanResults.TotalHits != luceneResults.TotalHits
            || leanDocumentIds.Length != leanResults.TotalHits
            || luceneDocumentIds.Length != luceneResults.TotalHits
            || !leanDocumentIds.SequenceEqual(luceneDocumentIds))
        {
            throw new InvalidOperationException(
                $"Phrase parity failed for {PhraseType}: LeanCorpus returned {leanResults.TotalHits} hits and Lucene.NET returned {luceneResults.TotalHits} hits.");
        }
    }
}

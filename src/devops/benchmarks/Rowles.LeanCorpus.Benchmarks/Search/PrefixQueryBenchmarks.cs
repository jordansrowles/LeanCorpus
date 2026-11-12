using BenchmarkDotNet.Attributes;
using Lucene.Net.Index;
using Lucene.Net.Search;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures PrefixQuery performance across LeanCorpus and Lucene.NET.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class PrefixQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("gov", "pres", "mark")]
    public string QueryPrefix { get; set; } = "gov";

    private LeanIndexSearcher? _leanSearcher;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _leanSearcher = SharedStandardIndex.LeanSearcher;
        // A scored boolean rewrite needs one clause per expanded term.
        Lucene.Net.Search.BooleanQuery.MaxClauseCount = Math.Max(Lucene.Net.Search.BooleanQuery.MaxClauseCount, 65_536);
        ValidateParity();
    }

    /// <summary>
    /// Proves the rows do the same logical work: both engines expand the same frozen term set, and
    /// every row returns the same number of matching documents.
    /// </summary>
    private void ValidateParity()
    {
        string qualified = string.Concat("body\0", QueryPrefix);
        var leanTerms = _leanSearcher!.GetSegmentReaders()
            .SelectMany(reader => reader.GetTermsWithPrefix(qualified))
            .Select(static term => term.Term[(term.Term.IndexOf('\0') + 1)..])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var luceneTerms = new List<string>();
        foreach (var leaf in SharedStandardIndex.LuceneSearcher.IndexReader.Leaves)
        {
            var terms = leaf.AtomicReader.GetTerms("body");
            if (terms is null)
                continue;
            var enumerator = terms.GetEnumerator();
            if (enumerator.SeekCeil(new Lucene.Net.Util.BytesRef(QueryPrefix)) == TermsEnum.SeekStatus.END)
                continue;
            do
            {
                string term = enumerator.Term.Utf8ToString();
                if (!term.StartsWith(QueryPrefix, StringComparison.Ordinal))
                    break;
                luceneTerms.Add(term);
            }
            while (enumerator.MoveNext());
        }
        string[] luceneSet = luceneTerms.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!leanTerms.SequenceEqual(luceneSet, StringComparer.Ordinal) || leanTerms.Length == 0)
            throw new InvalidOperationException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Prefix '{QueryPrefix}' expands to {leanTerms.Length} LeanCorpus and {luceneSet.Length} Lucene.NET terms."));

        int[] hits = [LeanCorpus_PrefixQuery_Scored(), LuceneNet_PrefixQuery_ConstantScore(), LuceneNet_PrefixQuery_Scored()];
        if (hits.Distinct().Count() != 1)
            throw new InvalidOperationException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Prefix '{QueryPrefix}' hit parity failed: LeanCorpus scored {hits[0]}, Lucene.NET constant-score {hits[1]}, scored {hits[2]}."));
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Benchmark preflight prefix {QueryPrefix}: terms={leanTerms.Length}, termSetSha256={BenchmarkSemanticProofs.Digest(string.Join("\n", leanTerms))}, hits={hits[0]}"));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // All resources are owned by SharedStandardIndex; do not dispose.
    }

    /// <summary>LeanCorpus prefix queries are scored: each expanded term contributes BM25.</summary>
    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_PrefixQuery_Scored()
    {
        var query = new Rowles.LeanCorpus.Search.Queries.PrefixQuery("body", QueryPrefix);
        return _leanSearcher!.Search(query, TopN).TotalHits;
    }

    /// <summary>Lucene.NET's default rewrite is constant-score: not the same scoring work as LeanCorpus.</summary>
    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_PrefixQuery_ConstantScore()
    {
        var query = new Lucene.Net.Search.PrefixQuery(new Term("body", QueryPrefix));
        return SharedStandardIndex.LuceneSearcher.Search(query, TopN).TotalHits;
    }

    /// <summary>Lucene.NET scoring each expanded term, the work that matches LeanCorpus.</summary>
    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_PrefixQuery_Scored()
    {
        var query = new Lucene.Net.Search.PrefixQuery(new Term("body", QueryPrefix))
        {
            MultiTermRewriteMethod = MultiTermQuery.SCORING_BOOLEAN_QUERY_REWRITE
        };
        return SharedStandardIndex.LuceneSearcher.Search(query, TopN).TotalHits;
    }
}

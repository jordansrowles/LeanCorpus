using BenchmarkDotNet.Attributes;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares search latency across multiple <see cref="ISimilarity"/> scoring models
/// (BM25, TF-IDF, language models, and advanced variants) on the same query and index.
/// Lucene.NET parity is included for BM25, Dirichlet, and Jelinek-Mercer.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class SimilarityBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    // LeanCorpus state
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _bm25Searcher;
    private LeanIndexSearcher? _tfIdfSearcher;
    private LeanIndexSearcher? _bm25PlusSearcher;
    private LeanIndexSearcher? _bm25LSearcher;
    private LeanIndexSearcher? _tfIdfAugmentedSearcher;
    private LeanIndexSearcher? _tfIdfPivotedSearcher;
    private LeanIndexSearcher? _tfIdfDoubleNormSearcher;
    private LeanIndexSearcher? _dirichletSearcher;
    private LeanIndexSearcher? _jmSearcher;
    private LeanIndexSearcher? _absDiscountingSearcher;

    // Lucene.NET state
    private LuceneMMapDirectory? _luceneDirectory;
    private Lucene.Net.Index.DirectoryReader? _luceneReader;
    private LuceneIndexSearcher? _luceneBm25Searcher;
    private LuceneIndexSearcher? _luceneDirichletSearcher;
    private LuceneIndexSearcher? _luceneJMSearcher;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        try
        {
            _leanDirectory = SharedStandardIndex.CreateDirectory();
            CreateLeanSearchers(_leanDirectory);
            _luceneDirectory = SharedStandardIndex.CreateLuceneDirectory();
            CreateLuceneSearchers(_luceneDirectory);
        }
        catch
        {
            CleanupSearchers();
            _leanDirectory?.Dispose();
            _luceneReader?.Dispose();
            _luceneDirectory?.Dispose();
            throw;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        CleanupSearchers();
        _leanDirectory?.Dispose();

        _luceneReader?.Dispose();
        _luceneDirectory?.Dispose();
    }

    private void CleanupSearchers()
    {
        _bm25Searcher?.Dispose();
        _tfIdfSearcher?.Dispose();
        _bm25PlusSearcher?.Dispose();
        _bm25LSearcher?.Dispose();
        _tfIdfAugmentedSearcher?.Dispose();
        _tfIdfPivotedSearcher?.Dispose();
        _tfIdfDoubleNormSearcher?.Dispose();
        _dirichletSearcher?.Dispose();
        _jmSearcher?.Dispose();
        _absDiscountingSearcher?.Dispose();
    }

    // --- Baseline  ---

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("bm25")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Bm25_TermQuery()
        => _bm25Searcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("bm25")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Bm25_TermQuery()
    {
        var q = new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("body", "government"));
        return _luceneBm25Searcher!.Search(q, TopN).TotalHits;
    }

    // --- Classic TF-IDF  ---

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_TfIdf_TermQuery()
        => _tfIdfSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    // --- Language model (with Lucene.NET parity) ---

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("lm")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Dirichlet_TermQuery()
        => _dirichletSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("lm")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Dirichlet_TermQuery()
    {
        var q = new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("body", "government"));
        return _luceneDirichletSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("lm")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_JelinekMercer_TermQuery()
        => _jmSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("lm")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_JelinekMercer_TermQuery()
    {
        var q = new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("body", "government"));
        return _luceneJMSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("lm")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_AbsoluteDiscounting_TermQuery()
        => _absDiscountingSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    // --- Advanced variants (LeanCorpus only) ---

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("variant")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Bm25Plus_TermQuery()
        => _bm25PlusSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("variant")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Bm25L_TermQuery()
        => _bm25LSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("variant")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_TfIdfAugmented_TermQuery()
        => _tfIdfAugmentedSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("variant")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_TfIdfPivoted_TermQuery()
        => _tfIdfPivotedSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("variant")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_TfIdfDoubleNorm_TermQuery()
        => _tfIdfDoubleNormSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    // --- Boolean query variants ---

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("bm25")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Bm25_BooleanQuery()
    {
        var builder = new BooleanQuery.Builder();
        builder.Add(new TermQuery("body", "government"), Rowles.LeanCorpus.Search.Occur.Must);
        builder.Add(new TermQuery("body", "market"), Rowles.LeanCorpus.Search.Occur.Should);
        return _bm25Searcher!.Search(builder.Build(), TopN).TotalHits;
    }

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("bm25")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Bm25_BooleanQuery()
    {
        var q = new Lucene.Net.Search.BooleanQuery
        {
            { new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("body", "government")), Lucene.Net.Search.Occur.MUST },
            { new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("body", "market")), Lucene.Net.Search.Occur.SHOULD }
        };
        return _luceneBm25Searcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_TfIdf_BooleanQuery()
    {
        var builder = new BooleanQuery.Builder();
        builder.Add(new TermQuery("body", "government"), Rowles.LeanCorpus.Search.Occur.Must);
        builder.Add(new TermQuery("body", "market"), Rowles.LeanCorpus.Search.Occur.Should);
        return _tfIdfSearcher!.Search(builder.Build(), TopN).TotalHits;
    }

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("lm")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Dirichlet_BooleanQuery()
    {
        var builder = new BooleanQuery.Builder();
        builder.Add(new TermQuery("body", "government"), Rowles.LeanCorpus.Search.Occur.Must);
        builder.Add(new TermQuery("body", "market"), Rowles.LeanCorpus.Search.Occur.Should);
        return _dirichletSearcher!.Search(builder.Build(), TopN).TotalHits;
    }

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("variant")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Bm25Plus_BooleanQuery()
    {
        var builder = new BooleanQuery.Builder();
        builder.Add(new TermQuery("body", "government"), Rowles.LeanCorpus.Search.Occur.Must);
        builder.Add(new TermQuery("body", "market"), Rowles.LeanCorpus.Search.Occur.Should);
        return _bm25PlusSearcher!.Search(builder.Build(), TopN).TotalHits;
    }

    // --- Index builders ---

    private void CreateLeanSearchers(LeanMMapDirectory directory)
    {
        _bm25Searcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = Bm25Similarity.Instance });

        _tfIdfSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = TfIdfSimilarity.Instance });

        _bm25PlusSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = Bm25PlusSimilarity.Instance });

        _bm25LSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = Bm25LSimilarity.Instance });

        _tfIdfAugmentedSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = TfIdfAugmentedSimilarity.Instance });

        _tfIdfPivotedSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = TfIdfPivotedSimilarity.Instance });

        _tfIdfDoubleNormSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = TfIdfDoubleNormSimilarity.Instance });

        _dirichletSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = DirichletSimilarity.Instance });

        _jmSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = LMJelinekMercerSimilarity.Instance });

        _absDiscountingSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = LMAbsoluteDiscountingSimilarity.Instance });
    }

    private void CreateLuceneSearchers(LuceneMMapDirectory directory)
    {
        _luceneReader = Lucene.Net.Index.DirectoryReader.Open(directory);
        _luceneBm25Searcher = new LuceneIndexSearcher(_luceneReader)
            { Similarity = new Lucene.Net.Search.Similarities.BM25Similarity(1.2f, 0.75f) };
        _luceneDirichletSearcher = new LuceneIndexSearcher(_luceneReader)
            { Similarity = new Lucene.Net.Search.Similarities.LMDirichletSimilarity(2000f) };
        _luceneJMSearcher = new LuceneIndexSearcher(_luceneReader)
            { Similarity = new Lucene.Net.Search.Similarities.LMJelinekMercerSimilarity(0.1f) };
    }
}

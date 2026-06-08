using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares search latency across multiple <see cref="ISimilarity"/> scoring models
/// (BM25, TF-IDF, language models, and advanced variants) on the same query and index.
/// Lucene.NET parity is included for Dirichlet and Jelinek-Mercer language models.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class SimilarityBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    // LeanCorpus state
    private string _leanIndexPath = string.Empty;
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
    private string _luceneIndexPath = string.Empty;
    private LuceneMMapDirectory? _luceneDirectory;
    private Lucene.Net.Index.DirectoryReader? _luceneReader;
    private StandardAnalyzer? _luceneAnalyzer;
    private LuceneIndexSearcher? _luceneDirichletSearcher;
    private LuceneIndexSearcher? _luceneJMSearcher;

    [GlobalSetup]
    public void Setup()
    {
        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        BuildLeanIndex(documents);
        BuildLuceneIndex(documents);
    }

    [GlobalCleanup]
    public void Cleanup()
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
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);

        _luceneReader?.Dispose();
        _luceneAnalyzer?.Dispose();
        _luceneDirectory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_luceneIndexPath) && IODirectory.Exists(_luceneIndexPath))
            IODirectory.Delete(_luceneIndexPath, recursive: true);
    }

    // --- Baseline  ---

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("similarity")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Bm25_TermQuery()
        => _bm25Searcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

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

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(Path.GetTempPath(), $"leancorpus-bench-similarity-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new IndexWriter(
            _leanDirectory,
            new IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        for (int i = 0; i < documents.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", documents[i]));
            writer.AddDocument(doc);
        }
        writer.Commit();

        _bm25Searcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { Similarity = Bm25Similarity.Instance });

        _tfIdfSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { Similarity = TfIdfSimilarity.Instance });

        _bm25PlusSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { Similarity = Bm25PlusSimilarity.Instance });

        _bm25LSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { Similarity = Bm25LSimilarity.Instance });

        _tfIdfAugmentedSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { Similarity = TfIdfAugmentedSimilarity.Instance });

        _tfIdfPivotedSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { Similarity = TfIdfPivotedSimilarity.Instance });

        _tfIdfDoubleNormSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { Similarity = TfIdfDoubleNormSimilarity.Instance });

        _dirichletSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { Similarity = DirichletSimilarity.Instance });

        _jmSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { Similarity = LMJelinekMercerSimilarity.Instance });

        _absDiscountingSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { Similarity = LMAbsoluteDiscountingSimilarity.Instance });
    }

    private void BuildLuceneIndex(string[] documents)
    {
        _luceneIndexPath = Path.Combine(Path.GetTempPath(), $"lucenenet-bench-similarity-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_luceneIndexPath);

        _luceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(_luceneIndexPath));
        _luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);

        using var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, _luceneAnalyzer));
        for (int i = 0; i < documents.Length; i++)
        {
            var doc = new Lucene.Net.Documents.Document
            {
                new LuceneStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture), Lucene.Net.Documents.Field.Store.NO),
                new LuceneTextField("body", documents[i], Lucene.Net.Documents.Field.Store.NO)
            };
            writer.AddDocument(doc);
        }
        writer.Commit();

        _luceneReader = Lucene.Net.Index.DirectoryReader.Open(_luceneDirectory);
        _luceneDirichletSearcher = new LuceneIndexSearcher(_luceneReader)
            { Similarity = new Lucene.Net.Search.Similarities.LMDirichletSimilarity(2000f) };
        _luceneJMSearcher = new LuceneIndexSearcher(_luceneReader)
            { Similarity = new Lucene.Net.Search.Similarities.LMJelinekMercerSimilarity(0.1f) };
    }
}

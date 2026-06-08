using BenchmarkDotNet.Attributes;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="MoreLikeThisQuery"/> against Lucene.NET <c>MoreLikeThis</c>
/// across <c>MaxQueryTerms</c> and <c>MinDocFreq</c> settings. Both indexes store term vectors.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class MoreLikeThisBenchmarks
{
    private const int TopN = 25;
    private const int SourceDocId = 0;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    /// <summary>Maximum query terms extracted from the source document.</summary>
    [Params(10, 25, 50)]
    public int MaxQueryTerms { get; set; } = 25;

    // LeanCorpus state
    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;

    // Lucene.NET state
    private string _luceneIndexPath = string.Empty;
    private Lucene.Net.Store.MMapDirectory? _luceneDirectory;
    private Lucene.Net.Analysis.Standard.StandardAnalyzer? _luceneAnalyzer;
    private Lucene.Net.Index.DirectoryReader? _luceneReader;
    private Lucene.Net.Search.IndexSearcher? _luceneSearcher;

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
        _leanSearcher?.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);

        _luceneReader?.Dispose();
        _luceneAnalyzer?.Dispose();
        _luceneDirectory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_luceneIndexPath) && IODirectory.Exists(_luceneIndexPath))
            IODirectory.Delete(_luceneIndexPath, recursive: true);
    }

    // --- LeanCorpus benchmarks (existing) ---

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MoreLikeThisQuery_DefaultParams()
    {
        var q = new MoreLikeThisQuery(
            SourceDocId,
            ["body"],
            new MoreLikeThisParameters
            {
                MaxQueryTerms = MaxQueryTerms,
                MinTermFreq = 1,
                MinDocFreq = 1
            });
        return _leanSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MoreLikeThisQuery_HighMinDocFreq()
    {
        var q = new MoreLikeThisQuery(
            SourceDocId,
            ["body"],
            new MoreLikeThisParameters
            {
                MaxQueryTerms = MaxQueryTerms,
                MinTermFreq = 2,
                MinDocFreq = 5
            });
        return _leanSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MoreLikeThisQuery_NoBoost()
    {
        var q = new MoreLikeThisQuery(
            SourceDocId,
            ["body"],
            new MoreLikeThisParameters
            {
                MaxQueryTerms = MaxQueryTerms,
                MinTermFreq = 1,
                MinDocFreq = 1,
                BoostByScore = false
            });
        return _leanSearcher!.Search(q, TopN).TotalHits;
    }

    // --- Lucene.NET parity benchmarks ---

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_MoreLikeThis_DefaultParams()
    {
        var mlt = new Lucene.Net.Queries.Mlt.MoreLikeThis(_luceneReader!);
        mlt.MinTermFreq = 1;
        mlt.MinDocFreq = 1;
        mlt.MinWordLen = 3;
        mlt.MaxQueryTerms = MaxQueryTerms;
        mlt.ApplyBoost = true;

        var query = mlt.Like(SourceDocId);
        return _luceneSearcher!.Search(query, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_MoreLikeThis_HighMinDocFreq()
    {
        var mlt = new Lucene.Net.Queries.Mlt.MoreLikeThis(_luceneReader!);
        mlt.MinTermFreq = 2;
        mlt.MinDocFreq = 5;
        mlt.MinWordLen = 3;
        mlt.MaxQueryTerms = MaxQueryTerms;
        mlt.ApplyBoost = true;

        var query = mlt.Like(SourceDocId);
        return _luceneSearcher!.Search(query, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_MoreLikeThis_NoBoost()
    {
        var mlt = new Lucene.Net.Queries.Mlt.MoreLikeThis(_luceneReader!);
        mlt.MinTermFreq = 1;
        mlt.MinDocFreq = 1;
        mlt.MinWordLen = 3;
        mlt.MaxQueryTerms = MaxQueryTerms;
        mlt.ApplyBoost = false;

        var query = mlt.Like(SourceDocId);
        return _luceneSearcher!.Search(query, TopN).TotalHits;
    }

    // --- Index builders ---

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(Path.GetTempPath(), $"leancorpus-bench-mlt-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new IndexWriter(
            _leanDirectory,
            new IndexWriterConfig
            {
                MaxBufferedDocs = 10_000,
                RamBufferSizeMB = 256,
                StoreTermVectors = true
            });
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

    private void BuildLuceneIndex(string[] documents)
    {
        _luceneIndexPath = Path.Combine(Path.GetTempPath(), $"lucenenet-bench-mlt-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_luceneIndexPath);

        _luceneDirectory = new Lucene.Net.Store.MMapDirectory(new System.IO.DirectoryInfo(_luceneIndexPath));
        _luceneAnalyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48);

        using (var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(
                Lucene.Net.Util.LuceneVersion.LUCENE_48, _luceneAnalyzer)))
        {
            for (int i = 0; i < documents.Length; i++)
            {
                // Use FieldType to enable term vectors without deprecated API.
                var fieldType = new Lucene.Net.Documents.FieldType
                {
                    IsIndexed = true,
                    IsTokenized = true,
                    IsStored = false,
                    StoreTermVectors = true
                };
                var doc = new Lucene.Net.Documents.Document
                {
                    new Lucene.Net.Documents.StringField(
                        "id",
                        i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Lucene.Net.Documents.Field.Store.NO),
                    new Lucene.Net.Documents.Field("body", documents[i], fieldType)
                };
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        _luceneReader = Lucene.Net.Index.DirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new Lucene.Net.Search.IndexSearcher(_luceneReader);
    }
}

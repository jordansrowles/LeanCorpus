using BenchmarkDotNet.Attributes;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanIndexSearcherConfig = Rowles.LeanCorpus.Search.Searcher.IndexSearcherConfig;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="MoreLikeThisQuery"/> performance on a deliberately single-segment index
/// to isolate segment-topology effects on WAND and scalar scoring.
/// <para>
/// LeanCorpus caches extracted MLT terms by (docId, MaxQueryTerms, MinTermFreq, MinDocFreq, MinWordLength).
/// After the first invocation, LeanCorpus measures cached extraction plus search.
/// Lucene.NET creates a new MoreLikeThis object on every call and measures
/// cold extraction, query construction, weight creation, and search each time.
/// Scalar and WAND use independently configured searchers. BenchmarkDotNet warms
/// each method independently, so both steady-state measurements use cached term extraction.
/// </para>
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class MoreLikeThisSingleSegmentBenchmarks
{
    private const int TopN = 25;
    private const int SourceDocId = 100;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    /// <summary>Maximum query terms extracted from the source document.</summary>
    public int MaxQueryTerms { get; set; } = 25;

    // LeanCorpus state
    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;
    private LeanIndexSearcher? _leanWandSearcher;

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
        _leanWandSearcher?.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);

        _luceneReader?.Dispose();
        _luceneAnalyzer?.Dispose();
        _luceneDirectory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_luceneIndexPath) && IODirectory.Exists(_luceneIndexPath))
            IODirectory.Delete(_luceneIndexPath, recursive: true);
    }

    // --- LeanCorpus scalar benchmarks ---

    [Benchmark(Baseline = true, Description = "LC MLT SingleSeg Scalar")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MoreLikeThisQuery_Scalar()
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

    // --- LeanCorpus WAND benchmark ---

    [Benchmark(Description = "LC MLT SingleSeg WAND")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MoreLikeThisQuery_Wand()
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
        return _leanWandSearcher!.Search(q, TopN).TotalHits;
    }

    // --- Lucene.NET parity benchmark ---

    [Benchmark(Description = "Lucene.NET MLT SingleSeg")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_MoreLikeThisQuery()
    {
        var mlt = new Lucene.Net.Queries.Mlt.MoreLikeThis(_luceneReader!);
        mlt.MinTermFreq = 1;
        mlt.MinDocFreq = 1;
        mlt.MinWordLen = 3;
        mlt.MaxQueryTerms = MaxQueryTerms;
        mlt.ApplyBoost = true;
        mlt.FieldNames = ["body"];

        var query = mlt.Like(SourceDocId);
        System.Diagnostics.Debug.Assert(query is Lucene.Net.Search.BooleanQuery bq && bq.Clauses.Count > 0,
            "Lucene MLT generated an empty query. Did you forget FieldNames?");
        return _luceneSearcher!.Search(query, TopN).TotalHits;
    }

    // --- Index builders ---

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-mlt-ss-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new IndexWriter(
            _leanDirectory,
            new IndexWriterConfig
            {
                MaxBufferedDocs = DocumentCount + 1,
                RamBufferSizeMB = 1024,
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

        var segmentCount = _leanSearcher.GetIndexSize().SegmentCount;
        if (segmentCount != 1)
            throw new InvalidOperationException(
                $"Single-segment MLT benchmark expected 1 segment but found {segmentCount}.");

        _leanWandSearcher = new LeanIndexSearcher(_leanDirectory,
            new LeanIndexSearcherConfig { EnableBlockMaxWand = true });
    }

    private void BuildLuceneIndex(string[] documents)
    {
        _luceneIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-mlt-ss-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_luceneIndexPath);

        _luceneDirectory = new Lucene.Net.Store.MMapDirectory(new System.IO.DirectoryInfo(_luceneIndexPath));
        _luceneAnalyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48);

        using (var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(
                Lucene.Net.Util.LuceneVersion.LUCENE_48, _luceneAnalyzer)
            {
                MaxBufferedDocs = DocumentCount + 1,
                RAMBufferSizeMB = 1024
            }))
        {
            for (int i = 0; i < documents.Length; i++)
            {
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
        var leafCount = _luceneReader.Leaves.Count;
        if (leafCount != 1)
            throw new InvalidOperationException(
                $"Single-segment Lucene.NET MLT benchmark expected 1 leaf but found {leafCount}.");

        _luceneSearcher = new Lucene.Net.Search.IndexSearcher(_luceneReader);
    }
}

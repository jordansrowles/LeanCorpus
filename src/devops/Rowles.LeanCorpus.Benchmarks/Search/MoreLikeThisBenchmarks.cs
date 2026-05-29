using BenchmarkDotNet.Attributes;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="MoreLikeThisQuery"/> at different <c>MaxQueryTerms</c> and
/// <c>MinDocFreq</c> settings. Requires term vectors to be stored at index time.
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

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;

    [GlobalSetup]
    public void Setup()
    {
        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        BuildLeanIndex(documents);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MoreLikeThisQuery_DefaultParams()
    {
        var q = new MoreLikeThisQuery(
            SourceDocId,
            ["body"],
            new Rowles.LeanCorpus.Search.Queries.MoreLikeThisParameters
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
            new Rowles.LeanCorpus.Search.Queries.MoreLikeThisParameters
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
            new Rowles.LeanCorpus.Search.Queries.MoreLikeThisParameters
            {
                MaxQueryTerms = MaxQueryTerms,
                MinTermFreq = 1,
                MinDocFreq = 1,
                BoostByScore = false
            });
        return _leanSearcher!.Search(q, TopN).TotalHits;
    }

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(Path.GetTempPath(), $"leancorpus-bench-mlt-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig
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
}

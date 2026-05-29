using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Search.Searcher;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares parallel multi-segment search against sequential search.
/// The index is force-flushed multiple times to produce multiple segments.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class ParallelSearchBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    /// <summary>Approximate number of segments to force during index build.</summary>
    [Params(4, 8)]
    public int SegmentCount { get; set; } = 4;

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _parallelSearcher;
    private LeanIndexSearcher? _sequentialSearcher;

    [GlobalSetup]
    public void Setup()
    {
        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        BuildLeanIndex(documents);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _parallelSearcher?.Dispose();
        _sequentialSearcher?.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SequentialSearch()
        => _sequentialSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_ParallelSearch()
        => _parallelSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_ParallelSearch_BooleanQuery()
    {
        var builder = new Rowles.LeanCorpus.Search.Queries.BooleanQuery.Builder();
        builder.Add(new TermQuery("body", "government"), Rowles.LeanCorpus.Search.Occur.Must);
        builder.Add(new TermQuery("body", "market"), Rowles.LeanCorpus.Search.Occur.Should);
        builder.Add(new TermQuery("body", "people"), Rowles.LeanCorpus.Search.Occur.Should);
        return _parallelSearcher!.Search(builder.Build(), TopN).TotalHits;
    }

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(Path.GetTempPath(), $"leancorpus-bench-parallel-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);

        // Force multiple segments by flushing at MaxBufferedDocs boundaries
        int docsPerSegment = Math.Max(1, documents.Length / SegmentCount);

        using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig
            {
                MaxBufferedDocs = docsPerSegment,
                RamBufferSizeMB = 256
            });
        for (int i = 0; i < documents.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", documents[i]));
            writer.AddDocument(doc);
        }
        writer.Commit();

        _parallelSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { ParallelSearch = true });

        _sequentialSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { ParallelSearch = false });
    }
}

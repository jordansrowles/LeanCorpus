using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Separates metadata-only searcher opening from warm term search across bounded
/// segment-reader cache capacities.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class SegmentReaderCacheBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("One", "256", "All", "Over capacity")]
    public string CacheCapacity { get; set; } = "256";

    private string _indexPath = string.Empty;
    private MMapDirectory? _directory;
    private IndexSearcher? _warmSearcher;
    private TermQuery _query = null!;
    private int _cacheCapacity;

    [GlobalSetup]
    public void Setup()
    {
        _indexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-segment-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_indexPath);
        _directory = new MMapDirectory(_indexPath);

        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        int docsPerSegment = Math.Max(1, (documents.Length + 511) / 512);
        int segmentCount;
        using (var writer = new IndexWriter(_directory, new IndexWriterConfig
        {
            DefaultAnalyser = new StandardAnalyser(),
            MaxBufferedDocs = docsPerSegment,
            MergePolicy = NoMergePolicy.Instance,
            DurableCommits = false,
        }))
        {
            for (int i = 0; i < documents.Length; i++)
            {
                var document = new LeanDocument();
                document.Add(new StringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                document.Add(new TextField("body", documents[i]));
                writer.AddDocument(document);
            }
            writer.Commit();
            segmentCount = writer.GetNrtSegments().Count;
        }

        _cacheCapacity = CacheCapacity switch
        {
            "One" => 1,
            "256" => 256,
            "All" => segmentCount,
            "Over capacity" => segmentCount + 1,
            _ => throw new InvalidOperationException($"Unknown cache capacity '{CacheCapacity}'."),
        };

        _query = new TermQuery("body", "government");
        _warmSearcher = OpenSearcher();
        _warmSearcher.Search(_query, TopN);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _warmSearcher?.Dispose();
        _directory?.Dispose();
        BenchmarkHelpers.DeleteDirectory(_indexPath);
    }

    [Benchmark(Description = "Open metadata-only searcher")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int OpenMetadataSearcher()
    {
        using var searcher = OpenSearcher();
        return searcher.Stats.TotalDocCount;
    }

    [Benchmark(Description = "Warm term search")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int WarmTermSearch()
        => _warmSearcher!.Search(_query, TopN).TotalHits;

    private IndexSearcher OpenSearcher()
        => new(_directory!, new IndexSearcherConfig
        {
            EnableQueryCache = false,
            MaxCachedSegmentReaders = _cacheCapacity,
            ParallelSearch = false,
        });
}

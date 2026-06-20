using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Search.Searcher;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneDocument = Lucene.Net.Documents.Document;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneDirectoryReader = Lucene.Net.Index.DirectoryReader;
using LuceneRAMDirectory = Lucene.Net.Store.RAMDirectory;
using LuceneTermQuery = Lucene.Net.Search.TermQuery;
using LuceneTerm = Lucene.Net.Index.Term;
using TermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;

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

    // Index state — guarded by (DocumentCount, SegmentCount) key
    private static readonly System.Threading.Lock s_gate = new();
    private static (int docCount, int segCount) s_lastKey;
    private static bool s_built;
    private static string s_leanIndexPath = string.Empty;
    private static LeanIndexSearcher? s_parallelSearcher;
    private static LeanIndexSearcher? s_sequentialSearcher;

    // Lucene.NET state
    private static LuceneRAMDirectory? s_luceneDirectory;
    private static LuceneDirectoryReader? s_luceneReader;
    private static LuceneIndexSearcher? s_luceneSearcher;

    [GlobalSetup]
    public void Setup()
    {
        var key = (DocumentCount, SegmentCount);
        if (!s_built || s_lastKey != key)
        {
            lock (s_gate)
            {
                if (!s_built || s_lastKey != key)
                {
                    var documents = BenchmarkData.BuildDocuments(DocumentCount);
                    BuildLeanIndexStatic(documents);
                    s_lastKey = key;
                    s_built = true;
                }
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Static resources persist for class lifetime.
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SequentialSearch()
        => s_sequentialSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_ParallelSearch()
        => s_parallelSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_ParallelSearch_BooleanQuery()
    {
        var builder = new Rowles.LeanCorpus.Search.Queries.BooleanQuery.Builder();
        builder.Add(new TermQuery("body", "government"), Rowles.LeanCorpus.Search.Occur.Must);
        builder.Add(new TermQuery("body", "market"), Rowles.LeanCorpus.Search.Occur.Should);
        builder.Add(new TermQuery("body", "people"), Rowles.LeanCorpus.Search.Occur.Should);
        return s_parallelSearcher!.Search(builder.Build(), TopN).TotalHits;
    }

    [Benchmark(Description = "Lucene.NET sequential search")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_SequentialSearch()
        => s_luceneSearcher!.Search(
            new LuceneTermQuery(new LuceneTerm("body", "government")), TopN).TotalHits;

    /// <summary>Release static Lucene.NET resources.</summary>
    public static void CleanupLuceneResources()
    {
        s_luceneReader?.Dispose();
        s_luceneReader = null;
        s_luceneDirectory?.Dispose();
        s_luceneDirectory = null;
    }

    private void BuildLeanIndexStatic(string[] documents)
    {
        s_leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-parallel-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(s_leanIndexPath);
        var directory = new LeanMMapDirectory(s_leanIndexPath);

        // Force multiple segments by flushing at MaxBufferedDocs boundaries
        int docsPerSegment = Math.Max(1, documents.Length / SegmentCount);

        using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            directory,
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

        s_parallelSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { ParallelSearch = true });

        s_sequentialSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { ParallelSearch = false });

        // Build a comparable Lucene.NET index with multiple segments.
        s_luceneDirectory = new LuceneRAMDirectory();
        var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var lw = new Lucene.Net.Index.IndexWriter(
            s_luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser));
        for (int i = 0; i < documents.Length; i++)
        {
            var doc = new LuceneDocument();
            doc.Add(new LuceneStringField("id",
                i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneTextField("body", documents[i],
                Lucene.Net.Documents.Field.Store.NO));
            lw.AddDocument(doc);
        }
        lw.Commit();
        s_luceneReader = LuceneDirectoryReader.Open(s_luceneDirectory);
        s_luceneSearcher = new LuceneIndexSearcher(s_luceneReader);
    }
}

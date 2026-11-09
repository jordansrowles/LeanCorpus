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
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneTerm = Lucene.Net.Index.Term;
using LucenePhraseQuery = Lucene.Net.Search.PhraseQuery;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares parallel multi-segment search against sequential search.
/// The index is force-flushed multiple times to produce multiple segments. Both engines use
/// memory-mapped storage and flush by document count only, so each builds the requested segment count.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
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
    private static string s_luceneIndexPath = string.Empty;
    private static LuceneMMapDirectory? s_luceneDirectory;
    private static LuceneDirectoryReader? s_luceneReader;
    private static LuceneIndexSearcher? s_luceneSearcher;
    private static LuceneIndexSearcher? s_luceneExecutorSearcher;
    private static int s_expectedHits;

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

    [Benchmark(Baseline = true, Description = "LeanCorpus phrase sequential")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SequentialSearch()
        => s_sequentialSearcher!.Search(
            new Rowles.LeanCorpus.Search.Queries.PhraseQuery("body", "government", "market"), TopN).TotalHits;

    [Benchmark(Description = "LeanCorpus phrase parallel")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_ParallelSearch()
        => s_parallelSearcher!.Search(
            new Rowles.LeanCorpus.Search.Queries.PhraseQuery("body", "government", "market"), TopN).TotalHits;

    [Benchmark(Description = "Lucene.NET phrase sequential")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_SequentialSearch()
    {
        var query = new LucenePhraseQuery();
        query.Add(new LuceneTerm("body", "government"));
        query.Add(new LuceneTerm("body", "market"));
        return s_luceneSearcher!.Search(query, TopN).TotalHits;
    }

    [Benchmark(Description = "Lucene.NET phrase executor")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_ExecutorSearch()
    {
        var query = new LucenePhraseQuery();
        query.Add(new LuceneTerm("body", "government"));
        query.Add(new LuceneTerm("body", "market"));
        return s_luceneExecutorSearcher!.Search(query, TopN).TotalHits;
    }

    /// <summary>Release static Lucene.NET resources.</summary>
    public static void CleanupLuceneResources()
    {
        s_luceneReader?.Dispose();
        s_luceneReader = null;
        s_luceneDirectory?.Dispose();
        s_luceneDirectory = null;
        BenchmarkHelpers.DeleteDirectory(s_luceneIndexPath);
        s_luceneIndexPath = string.Empty;
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
                RamBufferSizeMB = 0,
                RamPerThreadHardLimitMB = 0,
                // The admission limit defaults to 20,000 documents and flushes when full, which
                // would cap segments below docsPerSegment. Keep it above one segment.
                MaxQueuedDocs = docsPerSegment * 2,
                MergePolicy = Rowles.LeanCorpus.Index.Indexer.NoMergePolicy.Instance,
            });
        for (int i = 0; i < documents.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", documents[i]));
            writer.AddDocument(doc);
        }
        writer.Commit();

        // Use separate directory instances for each searcher to avoid
        // any shared-state issues when two searchers are open on the
        // same index concurrently (e.g. eager DocValues loading or
        // MMapDirectory tracking).
        var dir2 = new LeanMMapDirectory(s_leanIndexPath);

        s_parallelSearcher = new LeanIndexSearcher(
            dir2,
            new IndexSearcherConfig { ParallelSearch = true });

        s_sequentialSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { ParallelSearch = false });

        // Build a comparable Lucene.NET index with multiple segments.
        s_luceneIndexPath = BenchmarkHelpers.CreateTempDirectory("lucenenet-bench-parallel");
        s_luceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(s_luceneIndexPath));
        var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        var luceneConfig = new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser)
        {
            MaxBufferedDocs = docsPerSegment,
            RAMBufferSizeMB = Lucene.Net.Index.IndexWriterConfig.DISABLE_AUTO_FLUSH,
            MergePolicy = Lucene.Net.Index.NoMergePolicy.NO_COMPOUND_FILES,
        };
        using var lw = new Lucene.Net.Index.IndexWriter(
            s_luceneDirectory,
            luceneConfig);
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
        s_luceneExecutorSearcher = new LuceneIndexSearcher(s_luceneReader, System.Threading.Tasks.TaskScheduler.Default);

        string fixture = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"parallel-{documents.Length}-segments-{SegmentCount}");
        var leanTopology = BenchmarkFixtureEvidence.RecordLeanCorpus(fixture, directory, s_sequentialSearcher);
        var luceneTopology = BenchmarkFixtureEvidence.RecordLucene(fixture, s_luceneDirectory, s_luceneReader);
        int expectedSegments = (documents.Length + docsPerSegment - 1) / docsPerSegment;
        if (leanTopology.SegmentCount != expectedSegments || luceneTopology.SegmentCount != expectedSegments)
            throw new InvalidOperationException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Parallel fixture expected {expectedSegments} segments for {documents.Length} documents at {docsPerSegment} per segment, but LeanCorpus has {leanTopology.SegmentCount} and Lucene.NET {luceneTopology.SegmentCount}."));

        s_expectedHits = (int)BenchmarkFixtureEvidence.RequireWork(
            fixture, "phrase government market", BenchmarkFixtureEvidence.LeanCorpusEngine, LeanCorpus_SequentialSearch());
        AssertHits(fixture, "LeanCorpus parallel", LeanCorpus_ParallelSearch());
        AssertHits(fixture, "Lucene.NET sequential", LuceneNet_SequentialSearch());
        AssertHits(fixture, "Lucene.NET executor", LuceneNet_ExecutorSearch());
    }

    private static void AssertHits(string fixture, string row, int hits)
    {
        if (hits != s_expectedHits)
            throw new InvalidOperationException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{fixture} row '{row}' returned {hits} hits; LeanCorpus sequential returned {s_expectedHits}."));
    }
}

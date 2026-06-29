using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using LeanTermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;
using Lucene.Net.Search.Join;
using Lucene.Net.Util;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanIndexWriter = Rowles.LeanCorpus.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures block-join indexing throughput without mixing it with query timings.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
[InvocationCount(1)]
public class BlockJoinIndexBenchmarks
{
    private const int ChildrenPerBlock = 3;

    public static IEnumerable<int> BlockCounts => BenchmarkData.GetDocCounts(500);

    [ParamsSource(nameof(BlockCounts))]
    public int BlockCount { get; set; }

    private (string ParentTitle, string[] ChildBodies)[] _blocks = [];

    [GlobalSetup]
    public void Setup()
    {
        _blocks = BenchmarkData.BuildParentChildBlocks(BlockCount, ChildrenPerBlock);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_IndexBlocks()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-block-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            var directory = new LeanMMapDirectory(path);
            using var writer = new LeanIndexWriter(directory, new LeanIndexWriterConfig
            {
                MaxBufferedDocs = 10_000,
                RamBufferSizeMB = 256
            });

            IndexLeanBlocks(writer, _blocks);
            writer.Commit();
            return _blocks.Length;
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(path);
        }
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_IndexBlocks()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-block-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            using var directory = new LuceneMMapDirectory(new DirectoryInfo(path));
            using var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            using var writer = new Lucene.Net.Index.IndexWriter(
                directory,
                new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser));

            IndexLuceneBlocks(writer, _blocks);
            writer.Commit();
            return _blocks.Length;
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(path);
        }
    }

    internal static void IndexLeanBlocks(
        LeanIndexWriter writer,
        IReadOnlyList<(string ParentTitle, string[] ChildBodies)> blocks)
    {
        foreach (var (parentTitle, childBodies) in blocks)
        {
            var block = new List<LeanDocument>();

            foreach (var childBody in childBodies)
            {
                var child = new LeanDocument();
                child.Add(new LeanTextField("body", childBody));
                child.Add(new LeanStringField("type", "child"));
                block.Add(child);
            }

            var parent = new LeanDocument();
            parent.Add(new LeanTextField("title", parentTitle));
            parent.Add(new LeanStringField("type", "parent"));
            block.Add(parent);

            writer.AddDocumentBlock(block);
        }
    }

    internal static void IndexLuceneBlocks(
        Lucene.Net.Index.IndexWriter writer,
        IReadOnlyList<(string ParentTitle, string[] ChildBodies)> blocks)
    {
        foreach (var (parentTitle, childBodies) in blocks)
        {
            var block = new List<Lucene.Net.Documents.Document>();

            foreach (var childBody in childBodies)
            {
                var child = new Lucene.Net.Documents.Document
                {
                    new LuceneTextField("body", childBody, Field.Store.NO),
                    new LuceneStringField("type", "child", Field.Store.YES)
                };
                block.Add(child);
            }

            var parent = new Lucene.Net.Documents.Document
            {
                new LuceneTextField("title", parentTitle, Field.Store.YES),
                new LuceneStringField("type", "parent", Field.Store.YES)
            };
            block.Add(parent);

            writer.AddDocuments(block);
        }
    }


}

/// <summary>
/// Measures the hot block-join query path on prebuilt indexes.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[SimpleJob]
public class BlockJoinSearchBenchmarks
{
    private const int TopN = 25;
    private const int ChildrenPerBlock = 3;

    public static IEnumerable<int> BlockCounts => BenchmarkData.GetDocCounts(500);

    [ParamsSource(nameof(BlockCounts))]
    public int BlockCount { get; set; }

    private (string ParentTitle, string[] ChildBodies)[] _blocks = [];

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;

    private string _luceneIndexPath = string.Empty;
    private LuceneMMapDirectory? _luceneDirectory;
    private DirectoryReader? _luceneReader;
    private Lucene.Net.Search.IndexSearcher? _luceneSearcher;
    private Filter? _luceneParentFilter;

    [GlobalSetup]
    public void Setup()
    {
        _blocks = BenchmarkData.BuildParentChildBlocks(BlockCount, ChildrenPerBlock);
        BuildLeanIndex();
        BuildLuceneIndex();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        _luceneReader?.Dispose();
        _luceneDirectory?.Dispose();

        BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
        BenchmarkHelpers.DeleteDirectory(_luceneIndexPath);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_BlockJoinQuery()
    {
        var childQuery = new LeanTermQuery("body", "said");
        var blockJoin = new Rowles.LeanCorpus.Search.Queries.BlockJoinQuery(childQuery);
        var topDocs = _leanSearcher!.Search(blockJoin, TopN);
        return topDocs.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_ToParentBlockJoinQuery()
    {
        var childQuery = new Lucene.Net.Search.TermQuery(new Term("body", "said"));
        var parentQuery = new ToParentBlockJoinQuery(childQuery, _luceneParentFilter!, Lucene.Net.Search.Join.ScoreMode.Max);
        var topDocs = _luceneSearcher!.Search(parentQuery, TopN);
        return topDocs.TotalHits;
    }

    private void BuildLeanIndex()
    {
        _leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-block-search-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_leanIndexPath);

        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using (var writer = new LeanIndexWriter(_leanDirectory, new LeanIndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256
        }))
        {
            BlockJoinIndexBenchmarks.IndexLeanBlocks(writer, _blocks);
            writer.Commit();
        }

        _leanSearcher = new LeanIndexSearcher(_leanDirectory);
    }

    private void BuildLuceneIndex()
    {
        _luceneIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-block-search-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_luceneIndexPath);

        _luceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(_luceneIndexPath));
        using var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);

        using (var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser)))
        {
            BlockJoinIndexBenchmarks.IndexLuceneBlocks(writer, _blocks);
            writer.Commit();
        }

        _luceneReader = DirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new Lucene.Net.Search.IndexSearcher(_luceneReader);
        _luceneParentFilter = new FixedBitSetCachingWrapperFilter(
            new QueryWrapperFilter(new Lucene.Net.Search.TermQuery(new Term("type", "parent"))));
    }
}

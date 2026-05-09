using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using LeanTermQuery = Rowles.LeanLucene.Search.Queries.TermQuery;
using Lucene.Net.Search.Join;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Rowles.LeanLucene.Store;
using LeanDocument = Rowles.LeanLucene.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanLucene.Search.Searcher.IndexSearcher;
using LeanIndexWriter = Rowles.LeanLucene.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanLucene.Index.Indexer.IndexWriterConfig;
using LeanMMapDirectory = Rowles.LeanLucene.Store.MMapDirectory;
using LeanStringField = Rowles.LeanLucene.Document.Fields.StringField;
using LeanTextField = Rowles.LeanLucene.Document.Fields.TextField;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Compares block-join query performance: LeanLucene BlockJoinQuery vs
/// Lucene.NET ToParentBlockJoinQuery.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[SimpleJob]
public class BlockJoinBenchmarks
{
    private const int TopN = 25;
    private const int ChildrenPerBlock = 3;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(500);

    [ParamsSource(nameof(DocCounts))]
    public int BlockCount { get; set; }

    private (string ParentTitle, string[] ChildBodies)[] _blocks = [];

    // LeanLucene state
    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;

    // Lucene.NET state
    private RAMDirectory? _luceneDirectory;
    private DirectoryReader? _luceneReader;
    private Lucene.Net.Search.IndexSearcher? _luceneSearcher;
    private Filter? _luceneParentFilter;

    [GlobalSetup]
    public void Setup()
    {
        _blocks = BenchmarkData.BuildParentChildBlocks(BlockCount, ChildrenPerBlock);
        BuildLeanLuceneIndex();
        BuildLuceneNetIndex();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && System.IO.Directory.Exists(_leanIndexPath))
            System.IO.Directory.Delete(_leanIndexPath, recursive: true);

        _luceneReader?.Dispose();
        _luceneDirectory?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_IndexBlocks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leanlucene-bench-block-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(path);

        try
        {
            var directory = new LeanMMapDirectory(path);
            using var writer = new LeanIndexWriter(directory, new LeanIndexWriterConfig
            {
                MaxBufferedDocs = 10_000,
                RamBufferSizeMB = 256
            });

            IndexLeanBlocks(writer);
            writer.Commit();
            return _blocks.Length;
        }
        finally
        {
            if (System.IO.Directory.Exists(path))
                System.IO.Directory.Delete(path, recursive: true);
        }
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_BlockJoinQuery()
    {
        var childQuery = new LeanTermQuery("body", "said");
        var blockJoin = new BlockJoinQuery(childQuery);
        var topDocs = _leanSearcher!.Search(blockJoin, TopN);
        return topDocs.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_IndexBlocks()
    {
        using var directory = new RAMDirectory();
        using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new Lucene.Net.Index.IndexWriter(directory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer));

        foreach (var (parentTitle, childBodies) in _blocks)
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

        writer.Commit();
        return _blocks.Length;
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

    private void IndexLeanBlocks(LeanIndexWriter writer)
    {
        foreach (var (parentTitle, childBodies) in _blocks)
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

    private void BuildLeanLuceneIndex()
    {
        _leanIndexPath = Path.Combine(Path.GetTempPath(), $"leanlucene-bench-block-s-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(_leanIndexPath);

        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using (var writer = new LeanIndexWriter(_leanDirectory, new LeanIndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256
        }))
        {
            IndexLeanBlocks(writer);
            writer.Commit();
        }
        _leanSearcher = new LeanIndexSearcher(_leanDirectory);
    }

    private void BuildLuceneNetIndex()
    {
        _luceneDirectory = new RAMDirectory();
        using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);

        using (var writer = new Lucene.Net.Index.IndexWriter(_luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer)))
        {
            foreach (var (parentTitle, childBodies) in _blocks)
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
            writer.Commit();
        }

        _luceneReader = DirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new Lucene.Net.Search.IndexSearcher(_luceneReader);

        // Parent filter: docs with type=parent
        _luceneParentFilter = new FixedBitSetCachingWrapperFilter(
            new QueryWrapperFilter(new Lucene.Net.Search.TermQuery(new Term("type", "parent"))));
    }
}

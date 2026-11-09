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
/// Units: <see cref="BlockCount"/> blocks, each one parent and three children, so four
/// physical documents per block. Both engines use the matched field profile.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[WarmupCount(1)]
[IterationCount(3)]
[InvocationCount(1)]
public class BlockJoinIndexBenchmarks
{
    private const int ChildrenPerBlock = 3;

    public static IEnumerable<int> BlockCounts => BenchmarkData.GetDocCounts(500);

    [ParamsSource(nameof(BlockCounts))]
    public int BlockCount { get; set; }

    private (string ParentTitle, string[] ChildBodies)[] _blocks = [];
    private readonly List<string> _iterationPaths = [];

    [GlobalSetup]
    public void Setup()
    {
        _blocks = BenchmarkData.BuildParentChildBlocks(BlockCount, ChildrenPerBlock);
    }

    [GlobalCleanup]
    public void Cleanup() => CleanupIterationPaths();

    [IterationCleanup]
    public void CleanupIterationPaths()
    {
        foreach (var path in _iterationPaths)
            BenchmarkHelpers.DeleteDirectory(path);
        _iterationPaths.Clear();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_IndexBlocks()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-block-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var directory = new LeanMMapDirectory(path);
        using var writer = new LeanIndexWriter(directory, new LeanIndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256
        });

        IndexLeanBlocks(writer, _blocks);
        writer.Commit();
        return _blocks.Length;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_IndexBlocks()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-block-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var directory = new LuceneMMapDirectory(new DirectoryInfo(path));
        using var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new Lucene.Net.Index.IndexWriter(
            directory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser));

        IndexLuceneBlocks(writer, _blocks);
        writer.Commit();
        return _blocks.Length;
    }

    internal static void IndexLeanBlocks(
        LeanIndexWriter writer,
        IReadOnlyList<(string ParentTitle, string[] ChildBodies)> blocks)
    {
        // Matched field profile (ADR045): child body is indexed but not stored and type has no
        // DocValues, as in the Lucene.NET fixture.
        for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            var (parentTitle, childBodies) = blocks[blockIndex];
            var block = new List<LeanDocument>();

            for (int childIndex = 0; childIndex < childBodies.Length; childIndex++)
            {
                var child = new LeanDocument();
                child.Add(new LeanStringField("id", ChildId(blockIndex, childIndex), stored: true, boost: 1.0f, storeDocValues: false));
                child.Add(new LeanTextField("body", childBodies[childIndex], stored: false));
                child.Add(new LeanStringField("type", "child", stored: true, boost: 1.0f, storeDocValues: false));
                block.Add(child);
            }

            var parent = new LeanDocument();
            parent.Add(new LeanStringField("id", ParentId(blockIndex), stored: true, boost: 1.0f, storeDocValues: false));
            parent.Add(new LeanTextField("title", parentTitle));
            parent.Add(new LeanStringField("type", "parent", stored: true, boost: 1.0f, storeDocValues: false));
            block.Add(parent);

            writer.AddDocumentBlock(block);
        }
    }

    internal static void IndexLuceneBlocks(
        Lucene.Net.Index.IndexWriter writer,
        IReadOnlyList<(string ParentTitle, string[] ChildBodies)> blocks)
    {
        for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            var (parentTitle, childBodies) = blocks[blockIndex];
            var block = new List<Lucene.Net.Documents.Document>();

            for (int childIndex = 0; childIndex < childBodies.Length; childIndex++)
            {
                var child = new Lucene.Net.Documents.Document
                {
                    new LuceneStringField("id", ChildId(blockIndex, childIndex), Field.Store.YES),
                    new LuceneTextField("body", childBodies[childIndex], Field.Store.NO),
                    new LuceneStringField("type", "child", Field.Store.YES)
                };
                block.Add(child);
            }

            var parent = new Lucene.Net.Documents.Document
            {
                new LuceneStringField("id", ParentId(blockIndex), Field.Store.YES),
                new LuceneTextField("title", parentTitle, Field.Store.YES),
                new LuceneStringField("type", "parent", Field.Store.YES)
            };
            block.Add(parent);

            writer.AddDocuments(block);
        }
    }

    /// <summary>Stable logical ID of a block's parent document.</summary>
    internal static string ParentId(int blockIndex)
        => string.Concat("p", blockIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Stable logical ID of a child document.</summary>
    internal static string ChildId(int blockIndex, int childIndex)
        => string.Concat("c", blockIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), "-",
            childIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Derives the parents whose live children contain <paramref name="term"/> as a whitespace
    /// token, independently of both engines. Deleted parents and deleted children are excluded.
    /// </summary>
    internal static string[] ExpectedParents(
        IReadOnlyList<(string ParentTitle, string[] ChildBodies)> blocks,
        string term,
        IReadOnlySet<string>? deletedIds = null)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var parents = new List<string>();
        for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            if (deletedIds?.Contains(ParentId(blockIndex)) == true)
                continue;
            var children = blocks[blockIndex].ChildBodies;
            for (int childIndex = 0; childIndex < children.Length; childIndex++)
            {
                if (deletedIds?.Contains(ChildId(blockIndex, childIndex)) == true)
                    continue;
                if (children[childIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(token => string.Equals(token.ToLowerInvariant(), term, StringComparison.Ordinal)))
                {
                    parents.Add(ParentId(blockIndex));
                    break;
                }
            }
        }

        return parents.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Fails unless the results are exactly the expected parents: no child results, no duplicate
    /// parents and an exact total. Tie policy: parent scores are not compared and order is
    /// ignored, because Lucene.NET <c>ScoreMode.None</c> gives every parent the same score.
    /// </summary>
    internal static void ValidateParents(string engine, IReadOnlyList<string> expectedParents, IReadOnlyList<string> actualIds, int totalHits)
    {
        ArgumentNullException.ThrowIfNull(expectedParents);
        ArgumentNullException.ThrowIfNull(actualIds);
        BenchmarkFixtureEvidence.RequireWork("blockjoin", "parents", engine, totalHits);
        var children = actualIds.Where(static id => !id.StartsWith('p')).Take(5).ToArray();
        if (children.Length != 0)
            throw new InvalidOperationException($"Block join on {engine} returned child documents: {string.Join(",", children)}.");
        var duplicates = actualIds.GroupBy(static id => id, StringComparer.Ordinal).Where(static group => group.Count() > 1)
            .Select(static group => group.Key).Take(5).ToArray();
        if (duplicates.Length != 0)
            throw new InvalidOperationException($"Block join on {engine} returned duplicate parents: {string.Join(",", duplicates)}.");
        if (totalHits != expectedParents.Count || actualIds.Count != expectedParents.Count)
            throw new InvalidOperationException(
                $"Block join on {engine} returned {totalHits} total and {actualIds.Count} parents; the generated children give {expectedParents.Count}.");
        if (!actualIds.Order(StringComparer.Ordinal).SequenceEqual(expectedParents, StringComparer.Ordinal))
            throw new InvalidOperationException($"Block join on {engine} returned a different parent set from the generated children.");
    }


}

/// <summary>
/// Measures the hot block-join query path on prebuilt indexes.
/// Units: <see cref="BlockCount"/> blocks of four physical documents. Setup proves the parent
/// set against the generated child bodies on both engines.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
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

    private string TopologyFixture => string.Concat("blockjoin-search-", BlockCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [GlobalSetup]
    public void Setup()
    {
        _blocks = BenchmarkData.BuildParentChildBlocks(BlockCount, ChildrenPerBlock);
        BuildLeanIndex();
        BuildLuceneIndex();
        ProveParents();
    }

    private void ProveParents()
    {
        int documents = BlockCount * (ChildrenPerBlock + 1);
        BenchmarkFixtureEvidence.RecordProfile(TopologyFixture, "both engines", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["blocks"] = BlockCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["parents"] = BlockCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["children"] = (BlockCount * ChildrenPerBlock).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["documents"] = documents.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["child body"] = "indexed, not stored",
            ["type"] = "indexed, stored, no DocValues"
        });

        string[] expected = BlockJoinIndexBenchmarks.ExpectedParents(_blocks, "said");
        int limit = Math.Max(1, BlockCount);
        var lean = _leanSearcher!.Search(new Rowles.LeanCorpus.Search.Queries.BlockJoinQuery(new LeanTermQuery("body", "said")), limit);
        BlockJoinIndexBenchmarks.ValidateParents("LeanCorpus", expected,
            lean.ScoreDocs.Select(hit => _leanSearcher.GetStoredFields(hit.DocId)["id"][0]).ToArray(), lean.TotalHits);

        var lucene = _luceneSearcher!.Search(new ToParentBlockJoinQuery(
            new Lucene.Net.Search.TermQuery(new Term("body", "said")), _luceneParentFilter!, Lucene.Net.Search.Join.ScoreMode.None), limit);
        BlockJoinIndexBenchmarks.ValidateParents("Lucene.NET", expected,
            lucene.ScoreDocs.Select(hit => _luceneReader!.Document(hit.Doc).Get("id")
                ?? throw new InvalidOperationException("A Lucene.NET block-join result has no id.")).ToArray(), lucene.TotalHits);
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
        var parentQuery = new ToParentBlockJoinQuery(childQuery, _luceneParentFilter!, Lucene.Net.Search.Join.ScoreMode.None);
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
        BenchmarkFixtureEvidence.RecordLeanCorpus(TopologyFixture, _leanDirectory!, _leanSearcher);
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
        BenchmarkFixtureEvidence.RecordLucene(TopologyFixture, _luceneDirectory!, _luceneReader);
        _luceneSearcher = new Lucene.Net.Search.IndexSearcher(_luceneReader);
        _luceneParentFilter = new FixedBitSetCachingWrapperFilter(
            new QueryWrapperFilter(new Lucene.Net.Search.TermQuery(new Term("type", "parent"))));
    }
}

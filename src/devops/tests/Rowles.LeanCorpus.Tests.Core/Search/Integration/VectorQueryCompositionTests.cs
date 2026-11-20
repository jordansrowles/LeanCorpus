using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Tests.Core.Foundation.Unit;
using static Rowles.LeanCorpus.Tests.Core.Search.QueryCompositionCorpus;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Issue #121: <see cref="VectorQuery.TopK"/> selects one global candidate set per query instance
/// and reader snapshot. Every public route, and every enclosing Boolean, observes that same set.
/// </summary>
/// <remarks>
/// Documents v0 to v5 lie at 0, 10, 20, 30, 40 and 50 degrees from the query vector [1, 0], so
/// their exact scores descend in ordinal order. Groups alternate drop/keep from v0.
/// </remarks>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
[Collection(LeanCorpusDefaultsCollection.Name)]
public sealed class VectorQueryCompositionTests(TestDirectoryFixture fixture) : IClassFixture<TestDirectoryFixture>
{
    private static readonly float[] Target = [1f, 0f];
    private static readonly string[] Ordered = ["v0", "v1", "v2", "v3", "v4", "v5"];

    public static TheoryData<bool, int, int, int> TopKCases()
    {
        var data = new TheoryData<bool, int, int, int>();
        foreach (bool multiSegment in new[] { false, true })
            foreach (int topK in new[] { 1, 2 })
                foreach (int topN in new[] { 1, 10 })
                    foreach (int oversampling in new[] { 1, 2, -1 })
                        data.Add(multiSegment, topK, topN, oversampling);
        return data;
    }

    [Theory]
    [MemberData(nameof(TopKCases))]
    public void TopK_IsIndependentOfSearchTopNAndOversampling(bool multiSegment, int topK, int topN, int oversampling)
    {
        using var index = OpenVectors($"topk-{multiSegment}-{topK}-{topN}-{oversampling}", multiSegment);
        var searcher = index.Searcher;
        var query = new VectorQuery("vec", Target, topK, oversamplingFactor: oversampling);

        var results = searcher.Search(query, topN);
        Assert.Equal(Ordered.Take(Math.Min(topK, topN)), IdsInOrder(searcher, results));
        Assert.Equal(topK, results.TotalHits);
        Assert.Equal(topK, searcher.Count(query));
        Assert.Equal(topK, searcher.Search(query, 0).TotalHits);
    }

    [Fact]
    public void TopK_TiesPreferLowerGlobalDocId()
    {
        string path = SubDir("ties");
        using (var directory = new MMapDirectory(path))
        {
            using var writer = new IndexWriter(directory, new IndexWriterConfig());
            writer.AddDocument(VectorDoc("first", [1f, 1f], "keep", 0));
            writer.Commit();
            writer.AddDocument(VectorDoc("second", [1f, 1f], "keep", 1));
            writer.AddDocument(VectorDoc("third", [1f, 1f], "keep", 2));
            writer.Commit();
        }

        using var dir = new MMapDirectory(path);
        using var searcher = new IndexSearcher(dir);
        var top = searcher.Search(new VectorQuery("vec", [1f, 1f], topK: 2), 10);
        Assert.Equal(["first", "second"], IdsInOrder(searcher, top));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiblingFilter_DoesNotRefillTopK_ExplicitFilterDoes(bool multiSegment)
    {
        using var index = OpenVectors($"sibling-{multiSegment}", multiSegment);
        var searcher = index.Searcher;
        var keep = new TermQuery("group", "keep");

        // v0 is the nearest neighbour but is in the drop group.
        var nearest = new VectorQuery("vec", Target, topK: 1);
        Assert.Empty(searcher.Search(Bool((nearest, Occur.Must), (keep, Occur.Must)), 10).ScoreDocs);
        Assert.Equal(["v1"], Ids(searcher, searcher.Search(new VectorQuery("vec", Target, topK: 1, filter: keep), 10)));

        Assert.Equal(["v0", "v5"], Ids(searcher, searcher.Search(
            Bool((nearest, Occur.Should), (new TermQuery("id", "v5"), Occur.Should)), 10)));
        Assert.Equal(["v2", "v3", "v4", "v5"], Ids(searcher, searcher.Search(
            Bool((new TermQuery("type", "vector"), Occur.Must), (new VectorQuery("vec", Target, topK: 2), Occur.MustNot)), 10)));
        Assert.Equal(["v0"], Ids(searcher, searcher.Search(new ConstantScoreQuery(nearest), 10)));
        Assert.Equal(["v0"], Ids(searcher, searcher.Search(Bool((Bool((nearest, Occur.Must)), Occur.Must)), 10)));
    }

    [Fact]
    public void BlockJoinChildVector_UsesTheGlobalSelection()
    {
        string path = SubDir("block-join-vector");
        using (var directory = new MMapDirectory(path))
        {
            using var writer = new IndexWriter(directory, new IndexWriterConfig());
            writer.AddDocumentBlock([ChildVector([0f, 1f]), Parent("p1", "first parent", [1f, 0f])]);
            writer.Commit();
            writer.AddDocumentBlock([ChildVector([1f, 0.1f]), Parent("p2", "second parent", [1f, 0f])]);
            writer.AddDocumentBlock([ChildVector([1f, 0.2f]), Parent("p3", "third parent", [1f, 0f])]);
            writer.Commit();
        }

        using var dir = new MMapDirectory(path);
        using var searcher = new IndexSearcher(dir);
        var nearestChild = new VectorQuery("cvec", Target, topK: 1);
        Assert.Equal(["p2"], Ids(searcher, searcher.Search(new BlockJoinQuery(nearestChild), 10)));
        Assert.Equal(["p2"], Ids(searcher, searcher.Search(Bool((new BlockJoinQuery(nearestChild), Occur.Must)), 10)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparedResults_AreOperationAndInstanceScoped(bool enableCache)
    {
        using var index = OpenVectors($"scoped-{enableCache}", multiSegment: true,
            new IndexSearcherConfig { EnableQueryCache = enableCache });
        var searcher = index.Searcher;
        var keep = new VectorQuery("vec", Target, topK: 1, filter: new TermQuery("group", "keep"));
        var drop = new VectorQuery("vec", Target, topK: 1, filter: new TermQuery("group", "drop"));

        Assert.Equal(["v1"], Ids(searcher, searcher.Search(keep, 10)));
        Assert.Equal(["v0"], Ids(searcher, searcher.Search(drop, 10)));
        Assert.Equal(["v1"], Ids(searcher, searcher.Search(keep, 10)));
        Assert.Equal(["v0", "v1"], Ids(searcher, searcher.Search(Bool((keep, Occur.Should), (drop, Occur.Should)), 10)));

        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        Parallel.For(0, 64, i =>
        {
            var query = i % 2 == 0 ? keep : drop;
            string expected = i % 2 == 0 ? "v1" : "v0";
            string[] actual = Ids(searcher, searcher.Search(query, 10));
            if (actual is not [var only] || only != expected)
                errors.Enqueue($"{i}: expected {expected}, got [{string.Join(',', actual)}]");
        });
        Assert.Empty(errors);
    }

    [Fact]
    public void VectorQueryEquality_IncludesExecutionSettings()
    {
        var baseline = new VectorQuery("vec", Target, topK: 3);
        Assert.Equal(baseline, new VectorQuery("vec", [1f, 0f], topK: 3));
        Assert.Equal(baseline.GetHashCode(), new VectorQuery("vec", [1f, 0f], topK: 3).GetHashCode());
        Assert.Equal(new VectorQuery("vec", Target, topK: 3, filter: new TermQuery("group", "keep")),
            new VectorQuery("vec", Target, topK: 3, filter: new TermQuery("group", "keep")));

        Assert.NotEqual(baseline, new VectorQuery("vec", Target, topK: 3, filter: new TermQuery("group", "keep")));
        Assert.NotEqual(new VectorQuery("vec", Target, topK: 3, filter: new TermQuery("group", "keep")),
            new VectorQuery("vec", Target, topK: 3, filter: new TermQuery("group", "drop")));
        Assert.NotEqual(baseline, new VectorQuery("vec", Target, topK: 3, efSearch: 500));
        Assert.NotEqual(baseline, new VectorQuery("vec", Target, topK: 3, oversamplingFactor: 2));
        // Explicit one and the automatic sentinel behave differently for BBQ v3.
        Assert.NotEqual(baseline, new VectorQuery("vec", Target, topK: 3, oversamplingFactor: 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveTopK_IsRejected(int topK)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new VectorQuery("vec", Target, topK));

    [Fact]
    public void RefreshAndMerge_DoNotReusePreparedCandidates()
    {
        string path = SubDir("refresh");
        float[] probe = [MathF.Cos(MathF.PI / 36f), MathF.Sin(MathF.PI / 36f)];
        var query = new VectorQuery("vec", probe, topK: 1);
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        foreach (var (id, degrees) in new[] { ("v0", 0f), ("v1", 10f), ("v2", 20f) })
        {
            writer.AddDocument(VectorDoc(id, Angle(degrees), "keep", 0));
            writer.Commit();
        }

        using (var before = new IndexSearcher(directory))
            Assert.Equal(["v0"], Ids(before, before.Search(query, 10)));

        writer.AddDocument(VectorDoc("closest", probe, "keep", 0));
        writer.Commit();
        using (var refreshed = new IndexSearcher(directory))
            Assert.Equal(["closest"], Ids(refreshed, refreshed.Search(query, 10)));

        writer.ForceMerge(1);
        writer.Commit();
        using var merged = new IndexSearcher(directory);
        Assert.Single(merged.GetSegmentReaders());
        Assert.Equal(["closest"], Ids(merged, merged.Search(query, 10)));
        Assert.Equal(1, merged.Count(query));
    }

    [Fact]
    public void MultiReader_SelectsTopKAcrossAllComponents()
    {
        string first = SubDir("multi-first");
        string second = SubDir("multi-second");
        using (var directory = new MMapDirectory(first))
        {
            using var writer = new IndexWriter(directory, new IndexWriterConfig());
            writer.AddDocument(VectorDoc("a20", Angle(20), "keep", 0));
            writer.AddDocument(VectorDoc("a40", Angle(40), "keep", 0));
            writer.Commit();
        }
        using (var directory = new MMapDirectory(second))
        {
            using var writer = new IndexWriter(directory, new IndexWriterConfig());
            writer.AddDocument(VectorDoc("b10", Angle(10), "keep", 0));
            writer.AddDocument(VectorDoc("b30", Angle(30), "keep", 0));
            writer.Commit();
        }

        using var firstDirectory = new MMapDirectory(first);
        using var secondDirectory = new MMapDirectory(second);
        using var reader = new MultiReader([firstDirectory, secondDirectory]);

        // Composite IDs: a20 = 0, a40 = 1, b10 = 2, b30 = 3. b10 is nearest overall, then a20.
        var topOne = reader.Search(new VectorQuery("vec", Target, topK: 1), 10);
        Assert.Equal(1, topOne.TotalHits);
        Assert.Equal([2], topOne.ScoreDocs.Select(static doc => doc.DocId));

        var topTwo = reader.Search(Bool((new TermQuery("type", "vector"), Occur.Must), (new VectorQuery("vec", Target, topK: 2), Occur.Must)), 10);
        Assert.Equal(2, topTwo.TotalHits);
        Assert.Equal([2, 0], topTwo.ScoreDocs.Select(static doc => doc.DocId));

        var bareTopTwo = new VectorQuery("vec", Target, topK: 2);
        var firstPage = reader.Search(bareTopTwo, 1);
        Assert.Equal(2, firstPage.TotalHits);
        Assert.Equal([2], firstPage.ScoreDocs.Select(static doc => doc.DocId));
        var paged = reader.SearchAfter(firstPage.ScoreDocs[0], bareTopTwo, 10);
        Assert.Equal([0], paged.ScoreDocs.Select(static doc => doc.DocId));
    }

    [Fact]
    public void MultiReader_NestedVectorFilter_UsesTheGlobalInnerSelection()
    {
        using var multi = OpenMultiReader("multi-nested");
        var reader = multi.Reader;
        // Global inner top-2 is {b10 = 2, a20 = 0}. Nearest to 40 degrees within it is a20; within a
        // per-component inner selection it would be a40 = 1, which is outside the global set.
        var inner = new VectorQuery("vec", Target, topK: 2);
        var outer = new VectorQuery("vec", Angle(40), topK: 1, filter: inner);

        var innerFirst = reader.Search(Bool((inner, Occur.Should), (outer, Occur.Should)), 10);
        Assert.Equal([0, 2], innerFirst.ScoreDocs.Select(static doc => doc.DocId).Order());
        var outerFirst = reader.Search(Bool((outer, Occur.Should), (inner, Occur.Should)), 10);
        Assert.Equal([0, 2], outerFirst.ScoreDocs.Select(static doc => doc.DocId).Order());
        Assert.Equal([0], reader.Search(outer, 10).ScoreDocs.Select(static doc => doc.DocId));
    }

    [Fact]
    public void RewrittenVectorCopies_ShareOneSelection()
    {
        using var multi = OpenMultiReader("multi-rewrite");
        var reader = multi.Reader;
        var query = Bool((new TermQuery("type", "vector"), Occur.Must), (new FreshVectorQuery(), Occur.Must));
        var results = reader.Search(query, 10);
        Assert.Equal(1, results.TotalHits);
        Assert.Equal([2], results.ScoreDocs.Select(static doc => doc.DocId));

        using var index = OpenVectors("rewrite-single", multiSegment: true);
        Assert.Equal(["v0"], Ids(index.Searcher, index.Searcher.Search(query, 10)));
    }

    [Fact]
    public void MultiReader_UnsupportedComposition_ThrowsBeforeVectorSelection()
    {
        using var multi = OpenMultiReader("multi-unsupported");
        var reader = multi.Reader;
        var query = Bool(
            (new VectorQuery("vec", Target, topK: 1), Occur.Must),
            (new RrfQuery().Add(new TermQuery("type", "vector")), Occur.Should));
        Assert.Throws<NotSupportedException>(() => reader.Search(query, 10));
    }

    [Fact]
    public async Task SearchAsync_PreparesEachVectorQueryOnce()
    {
        var syncMetrics = new DefaultMetricsCollector();
        var asyncMetrics = new DefaultMetricsCollector();
        using var syncIndex = OpenVectors("async-once-sync", multiSegment: true,
            new IndexSearcherConfig { EnableQueryCache = false, Metrics = syncMetrics });
        using var asyncIndex = OpenVectors("async-once-async", multiSegment: true,
            new IndexSearcherConfig { EnableQueryCache = false, Metrics = asyncMetrics });
        var query = Bool((new VectorQuery("vec", Target, topK: 2), Occur.Must), (new TermQuery("type", "vector"), Occur.Must));

        syncIndex.Searcher.Search(query, 10);
        var streamed = new List<ScoreDoc>();
        await foreach (var doc in asyncIndex.Searcher.SearchAsync(query, new SearchOptions(), TestContext.Current.CancellationToken))
            streamed.Add(doc);

        Assert.Equal(["v0", "v1"], IdsOf(asyncIndex.Searcher, streamed));
        long expectedScans = syncMetrics.GetSnapshot().VectorExactScanCount;
        Assert.True(expectedScans > 1);
        Assert.Equal(expectedScans, asyncMetrics.GetSnapshot().VectorExactScanCount);
    }

    [Fact]
    public void CancellationDuringPreparation_DoesNotLeakIntoLaterCalls()
    {
        using var index = OpenVectors("cancel", multiSegment: true);
        var searcher = index.Searcher;
        var query = Bool((new VectorQuery("vec", Target, topK: 2), Occur.Must), (new TermQuery("type", "vector"), Occur.Must));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => searcher.Search(query, 10, cancelled.Token));
        var partial = searcher.Search(query, 10, new SearchOptions { CancellationToken = cancelled.Token });
        Assert.True(partial.IsPartial);
        Assert.Equal(["v0", "v1"], Ids(searcher, searcher.Search(query, 10)));
    }

    [Fact]
    public void RecursiveVectorPreparation_FailsExplicitly()
    {
        using var index = OpenVectors("recursive", multiSegment: false);
        var self = new SelfReferencingQuery();
        Assert.Throws<InvalidOperationException>(() => index.Searcher.Search(self, 10));
    }

    [Fact]
    public async Task EveryPublicRoute_ObservesTheSameSelectedSet()
    {
        using var index = OpenVectors("routes", multiSegment: true);
        var searcher = index.Searcher;
        var query = new VectorQuery("vec", Target, topK: 2);
        string[] expected = ["v0", "v1"];

        Assert.Equal(expected, Ids(searcher, searcher.Search(query, 10)));
        Assert.Equal(expected, Ids(searcher, searcher.Search(query, 10, CancellationToken.None)));
        Assert.Equal(expected, Ids(searcher, searcher.Search(query, 10, new SearchOptions())));
        Assert.Equal(expected, Ids(searcher, searcher.Search(query, 10, SortField.Int64("rank"))));
        Assert.Equal(expected, Ids(searcher, searcher.Search(query, 10, SortField.DocId)));
        Assert.Equal(2, searcher.Count(query));

        var page = searcher.Search(query, 1, SortField.DocId);
        var next = searcher.SearchAfter(page.ScoreDocs[0], query, 10, SortField.DocId);
        Assert.Equal(expected, Ids(searcher, page).Concat(Ids(searcher, next)).Order(StringComparer.Ordinal));

        var count = new CountCollector();
        ICollector boxed = count;
        searcher.Search(query, boxed);
        Assert.Equal(2, boxed.TotalHits);
        var listing = new ListingCollector();
        searcher.Search(query, listing);
        Assert.Equal(expected, listing.DocIds.Select(id => Id(searcher, id)).Order(StringComparer.Ordinal));

        Assert.Equal(expected, IdsOf(searcher, searcher.SearchStreaming(query)));
        Assert.Equal(expected, IdsOf(searcher, searcher.Search(query, new SearchOptions { StreamResults = true })));
        Assert.Equal(expected, IdsOf(searcher, searcher.Search(query, new SearchOptions())));
        var asyncDocs = new List<ScoreDoc>();
        await foreach (var doc in searcher.SearchAsync(query, new SearchOptions()))
            asyncDocs.Add(doc);
        Assert.Equal(expected, IdsOf(searcher, asyncDocs));

        var (facetDocs, facets) = searcher.SearchWithFacets(query, 10, "group");
        Assert.Equal(expected, Ids(searcher, facetDocs));
        Assert.Equal(2, Assert.Single(facets).Buckets.Sum(static bucket => bucket.Count));

        int outside = searcher.Search(new TermQuery("id", "v3"), 1).ScoreDocs[0].DocId;
        int inside = searcher.Search(new TermQuery("id", "v0"), 1).ScoreDocs[0].DocId;
        var outsideExplanation = searcher.Explain(query, outside);
        Assert.NotNull(outsideExplanation);
        Assert.Equal(0f, outsideExplanation.Score);
        Assert.Contains("TopK", outsideExplanation.Description, StringComparison.Ordinal);
        var insideExplanation = searcher.Explain(query, inside);
        Assert.NotNull(insideExplanation);
        Assert.True(insideExplanation.Score > 0f);
    }

    private OpenIndex OpenVectors(string name, bool multiSegment, IndexSearcherConfig? config = null)
    {
        var directory = new MMapDirectory(SubDir(name));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            for (int i = 0; i < Ordered.Length; i++)
            {
                writer.AddDocument(VectorDoc(Ordered[i], Angle(i * 10), i % 2 == 0 ? "drop" : "keep", Ordered.Length - i));
                if (multiSegment && i % 2 == 1)
                    writer.Commit();
            }
            writer.Commit();
        }

        return new OpenIndex(directory, config);
    }

    /// <summary>
    /// Two components with composite IDs a20 = 0, a40 = 1, b10 = 2 and b30 = 3; b10 is nearest to
    /// the target, then a20.
    /// </summary>
    private OpenMulti OpenMultiReader(string name)
    {
        string first = SubDir(name + "-first");
        string second = SubDir(name + "-second");
        using (var directory = new MMapDirectory(first))
        {
            using var writer = new IndexWriter(directory, new IndexWriterConfig());
            writer.AddDocument(VectorDoc("a20", Angle(20), "keep", 0));
            writer.AddDocument(VectorDoc("a40", Angle(40), "keep", 0));
            writer.Commit();
        }
        using (var directory = new MMapDirectory(second))
        {
            using var writer = new IndexWriter(directory, new IndexWriterConfig());
            writer.AddDocument(VectorDoc("b10", Angle(10), "keep", 0));
            writer.AddDocument(VectorDoc("b30", Angle(30), "keep", 0));
            writer.Commit();
        }

        return new OpenMulti([new MMapDirectory(first), new MMapDirectory(second)]);
    }

    private sealed class OpenMulti(MMapDirectory[] directories) : IDisposable
    {
        public MultiReader Reader { get; } = new(directories);

        public void Dispose()
        {
            Reader.Dispose();
            foreach (var directory in directories)
                directory.Dispose();
        }
    }

    private static float[] Angle(float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        return [MathF.Cos(radians), MathF.Sin(radians)];
    }

    private static LeanDocument VectorDoc(string id, float[] vector, string group, long rank)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id, stored: true));
        document.Add(new StringField("type", "vector"));
        document.Add(new StringField("group", group));
        document.Add(new Int64Field("rank", rank));
        document.Add(new VectorField("vec", vector));
        return document;
    }

    private static LeanDocument ChildVector(float[] vector)
    {
        var document = new LeanDocument();
        document.Add(new StringField("type", "child"));
        document.Add(new VectorField("cvec", vector));
        return document;
    }

    private static string[] IdsInOrder(IndexSearcher searcher, TopDocs docs)
        => docs.ScoreDocs.Select(doc => Id(searcher, doc.DocId)).ToArray();

    private static string[] IdsOf(IndexSearcher searcher, IEnumerable<ScoreDoc> docs)
        => docs.Select(doc => Id(searcher, doc.DocId)).Order(StringComparer.Ordinal).ToArray();

    private string SubDir(string name)
    {
        string path = Path.Combine(fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class OpenIndex(MMapDirectory directory, IndexSearcherConfig? config) : IDisposable
    {
        public IndexSearcher Searcher { get; } = config is null ? new(directory) : new(directory, config);

        public void Dispose()
        {
            Searcher.Dispose();
            directory.Dispose();
        }
    }

    private sealed class ListingCollector : ICollector
    {
        public List<int> DocIds { get; } = [];
        public int TotalHits => DocIds.Count;
        public void Collect(int docId, float score) => DocIds.Add(docId);
    }

    /// <summary>Rewrites to a new, equal vector query on every call.</summary>
    private sealed class FreshVectorQuery : Rowles.LeanCorpus.Search.Query
    {
        public override string Field => "vec";
        public override Rowles.LeanCorpus.Search.Query Rewrite() => new VectorQuery("vec", Target, topK: 1);
        public override bool Equals(object? obj) => obj is FreshVectorQuery;
        public override int GetHashCode() => typeof(FreshVectorQuery).GetHashCode();
    }

    /// <summary>Rewrites to a vector query whose filter is this query, forming a cycle.</summary>
    private sealed class SelfReferencingQuery : Rowles.LeanCorpus.Search.Query
    {
        private readonly VectorQuery _vector;

        public SelfReferencingQuery() => _vector = new VectorQuery("vec", Target, topK: 1, filter: this);

        public override string Field => "vec";
        public override Rowles.LeanCorpus.Search.Query Rewrite() => _vector;
        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
}

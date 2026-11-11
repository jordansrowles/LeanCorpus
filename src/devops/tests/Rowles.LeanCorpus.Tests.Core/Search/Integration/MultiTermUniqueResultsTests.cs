using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Verifies that multi-term queries return each document once, with <c>TotalHits</c> counting
/// unique documents (ADR048), and that regexp de-duplication adds no per-hit allocation.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class MultiTermUniqueResultsTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public MultiTermUniqueResultsTests(TestDirectoryFixture fixture) => _fixture = fixture;

    public static TheoryData<string> Families => new() { "regexp-prefix", "regexp-fallback", "regexp-contains", "prefix", "wildcard", "fuzzy" };

    [Theory(DisplayName = "Multi-Term Queries: Each Document Appears Once")]
    [MemberData(nameof(Families))]
    public void MultiTermQueries_EachDocumentAppearsOnce(string family)
    {
        // Every document holds two terms that each match the query, and a quarter hold neither.
        using var searcher = BuildIndex("unique_" + family + "_" + Guid.NewGuid().ToString("N"), 200, segments: 4);
        Query query = Create(family);

        var results = searcher.Search(query, 1_000, TestContext.Current.CancellationToken);

        int[] docIds = results.ScoreDocs.Select(static hit => hit.DocId).ToArray();
        Assert.Equal(docIds.Length, docIds.Distinct().Count());
        Assert.Equal(150, results.TotalHits);
        Assert.Equal(150, docIds.Length);
    }

    [Fact(DisplayName = "Regexp: Scores Are Summed Across Matching Terms")]
    public void Regexp_ScoresAreSummedAcrossMatchingTerms()
    {
        using var searcher = BuildIndex("unique_sum_" + Guid.NewGuid().ToString("N"), 8, segments: 1);

        var both = searcher.Search(new RegexpQuery("body", "gov.*"), 10, TestContext.Current.CancellationToken);
        var first = searcher.Search(new RegexpQuery("body", "government"), 10, TestContext.Current.CancellationToken);
        var second = searcher.Search(new RegexpQuery("body", "governor"), 10, TestContext.Current.CancellationToken);

        int docId = both.ScoreDocs[0].DocId;
        float expected = first.ScoreDocs.Single(hit => hit.DocId == docId).Score + second.ScoreDocs.Single(hit => hit.DocId == docId).Score;
        Assert.Equal(expected, both.ScoreDocs[0].Score, precision: 4);
    }

    [Fact(DisplayName = "Regexp: De-Duplication Adds No Per-Hit Allocation")]
    public void Regexp_DeDuplication_AddsNoPerHitAllocation()
    {
        using var small = BuildIndex("unique_alloc_small_" + Guid.NewGuid().ToString("N"), 200, segments: 1);
        using var large = BuildIndex("unique_alloc_large_" + Guid.NewGuid().ToString("N"), 4_000, segments: 1);
        var query = new RegexpQuery("body", "gov.*");

        long smallBytes = MeasureAllocation(small, query);
        long largeBytes = MeasureAllocation(large, query);

        // 20 times the matching documents must not cost materially more: no allocation per hit.
        Assert.True(largeBytes <= smallBytes * 2 + 16_384, $"200 docs: {smallBytes} B; 4,000 docs: {largeBytes} B.");
    }

    private static long MeasureAllocation(IndexSearcher searcher, Query query)
    {
        for (int i = 0; i < 3; i++)
            _ = searcher.Search(query, 10, TestContext.Current.CancellationToken);
        long before = GC.GetAllocatedBytesForCurrentThread();
        _ = searcher.Search(query, 10, TestContext.Current.CancellationToken);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static Query Create(string family) => family switch
    {
        "regexp-prefix" => new RegexpQuery("body", "gov.*"),
        "regexp-fallback" => new RegexpQuery("body", "(gov).*"),
        "regexp-contains" => new RegexpQuery("body", ".*overn.*"),
        "prefix" => new PrefixQuery("body", "gov"),
        "wildcard" => new WildcardQuery("body", "gov*"),
        "fuzzy" => new FuzzyQuery("body", "governer", 1),
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };

    private IndexSearcher BuildIndex(string name, int documentCount, int segments)
    {
        string path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = Math.Max(1, documentCount / segments),
            MergePolicy = NoMergePolicy.Instance
        }))
        {
            for (int i = 0; i < documentCount; i++)
            {
                var document = new LeanDocument();
                document.Add(new TextField("body", i % 4 == 3 ? "unrelated filler text" : "government governor filler"));
                writer.AddDocument(document);
            }
            writer.Commit();
        }
        return new IndexSearcher(directory);
    }
}

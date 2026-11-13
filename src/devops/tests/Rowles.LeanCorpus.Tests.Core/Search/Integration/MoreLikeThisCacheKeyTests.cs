using System.Globalization;
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
/// The MoreLikeThis extraction cache key covers the source document, fields, every frequency
/// limit and the word length, so no search can reuse an incompatible entry; caches are isolated
/// per searcher generation.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class MoreLikeThisCacheKeyTests : IClassFixture<TestDirectoryFixture>
{
    private const int DocumentCount = MoreLikeThisTestCorpus.DocumentCount;

    private readonly TestDirectoryFixture _fixture;

    public MoreLikeThisCacheKeyTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "MoreLikeThis: Each Extraction Parameter Has Its Own Cache Entry")]
    public void MoreLikeThis_EachExtractionParameter_HasItsOwnCacheEntry()
    {
        using var directory = Build("mlt_key", segments: 3);
        using var searcher = new IndexSearcher(directory);
        int docId = MoreLikeThisTestCorpus.DocId(searcher, 11);
        var baseline = new MoreLikeThisParameters { MaxQueryTerms = 6 };
        MoreLikeThisParameters[] variants =
        [
            baseline with { MaxQueryTerms = 3 },
            baseline with { MinTermFreq = 2 },
            baseline with { MinDocFreq = 30 },
            baseline with { MaxDocFreq = 60 },
            baseline with { MinWordLength = 6 }
        ];
        string[][] fieldSets = [["body"], ["title"], ["body", "title"]];

        // Warm the cache with the baseline over every field set, then check that every other
        // combination still matches its own fresh extraction rather than a reused entry.
        foreach (var fields in fieldSets)
            searcher.MoreLikeThis(docId, fields, 10, baseline);
        int nonEmpty = 0;
        foreach (var fields in fieldSets)
        foreach (var parameters in variants.Prepend(baseline))
        {
            var query = new MoreLikeThisQuery(docId, fields, parameters);
            using var fresh = new IndexSearcher(directory);
            var expected = fresh.MoreLikeThis(docId, fields, 10, parameters);
            var actual = searcher.MoreLikeThis(docId, fields, 10, parameters);
            Assert.Equal(expected.TotalHits, actual.TotalHits);
            Assert.Equal(expected.ScoreDocs.Select(static h => h.DocId), actual.ScoreDocs.Select(static h => h.DocId));
            var extracted = fresh.ExtractMoreLikeThisTerms(query);
            if (extracted.Length == 0)
            {
                // Nothing qualifies: no query runs and nothing is cached.
                Assert.Equal(0, actual.TotalHits);
                Assert.False(searcher.TryGetCachedMoreLikeThisTerms(query, out _));
                continue;
            }

            Assert.True(searcher.TryGetCachedMoreLikeThisTerms(query, out var cached));
            Assert.Equal(extracted, cached);
            nonEmpty++;
        }

        // The variants must exercise real extractions, not only empty ones.
        Assert.True(nonEmpty >= 12, $"only {nonEmpty} combinations extracted terms");

        // BoostByScore applies after lookup: both settings share one extraction entry, and a warm
        // search with either setting equals a fresh one.
        var boostedQuery = new MoreLikeThisQuery(docId, ["body"], baseline);
        var unboostedQuery = new MoreLikeThisQuery(docId, ["body"], baseline with { BoostByScore = false });
        Assert.True(searcher.TryGetCachedMoreLikeThisTerms(boostedQuery, out var shared));
        Assert.True(searcher.TryGetCachedMoreLikeThisTerms(unboostedQuery, out var sameEntry));
        Assert.Same(shared, sameEntry);
        var unboosted = searcher.MoreLikeThis(docId, ["body"], 10, baseline with { BoostByScore = false });
        using var freshUnboosted = new IndexSearcher(directory);
        var expectedUnboosted = freshUnboosted.MoreLikeThis(docId, ["body"], 10, baseline with { BoostByScore = false });
        Assert.Equal(expectedUnboosted.ScoreDocs.Select(static h => h.DocId), unboosted.ScoreDocs.Select(static h => h.DocId));
        Assert.Equal(expectedUnboosted.ScoreDocs.Select(static h => h.Score), unboosted.ScoreDocs.Select(static h => h.Score));
        var boosted = searcher.MoreLikeThis(docId, ["body"], 10, baseline);
        var boostedScores = boosted.ScoreDocs.ToDictionary(static hit => hit.DocId, static hit => hit.Score);
        Assert.Contains(unboosted.ScoreDocs, hit => boostedScores.TryGetValue(hit.DocId, out float score) && Math.Abs(score - hit.Score) > 1e-6f * Math.Max(1f, Math.Abs(hit.Score)));
    }

    [Fact(DisplayName = "MoreLikeThis: Cache Entries Do Not Cross Searcher Generations")]
    public void MoreLikeThis_CacheEntries_DoNotCrossGenerations()
    {
        string path = NewPath("mlt_generations");
        using var directory = new MMapDirectory(path);
        MoreLikeThisTestCorpus.Write(directory, 0, DocumentCount, segments: 2);
        using var first = new IndexSearcher(directory);
        int docId = 5;
        var parameters = new MoreLikeThisParameters { MaxQueryTerms = 8 };
        first.MoreLikeThis(docId, ["body"], 10, parameters);

        // Replace the document at that global identifier's position by deleting and re-adding.
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance, StoreTermVectors = true }))
        {
            writer.DeleteDocuments(new TermQuery("id", "0"));
            writer.Commit();
            writer.ForceMerge(1);
            writer.Commit();
        }

        using var second = new IndexSearcher(directory);
        var query = new MoreLikeThisQuery(docId, ["body"], parameters);
        Assert.False(second.TryGetCachedMoreLikeThisTerms(query, out _));
        var expected = second.ExtractMoreLikeThisTerms(query);
        second.MoreLikeThis(docId, ["body"], 10, parameters);
        Assert.True(second.TryGetCachedMoreLikeThisTerms(query, out var cached));
        Assert.Equal(expected, cached);
    }

    private MMapDirectory Build(string name, int segments) => MoreLikeThisTestCorpus.Build(_fixture.Path, name, segments);

    private string NewPath(string name) => MoreLikeThisTestCorpus.NewPath(_fixture.Path, name);
}

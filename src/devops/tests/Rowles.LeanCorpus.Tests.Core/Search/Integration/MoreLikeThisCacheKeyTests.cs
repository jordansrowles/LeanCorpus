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
    private const int DocumentCount = 400;
    private static readonly string[] Words =
    [
        "market", "government", "economy", "reform", "policy", "growth", "trade", "budget", "tax", "bank",
        "export", "import", "labour", "wages", "energy", "oil", "climate", "science", "school", "health",
        "court", "law", "vote", "party", "minister", "council", "city", "rural", "farm", "harvest"
    ];

    private readonly TestDirectoryFixture _fixture;

    public MoreLikeThisCacheKeyTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "MoreLikeThis: Each Extraction Parameter Has Its Own Cache Entry")]
    public void MoreLikeThis_EachExtractionParameter_HasItsOwnCacheEntry()
    {
        using var directory = Build("mlt_key", segments: 3);
        using var searcher = new IndexSearcher(directory);
        int docId = DocId(searcher, 11);
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
    }

    [Fact(DisplayName = "MoreLikeThis: Cache Entries Do Not Cross Searcher Generations")]
    public void MoreLikeThis_CacheEntries_DoNotCrossGenerations()
    {
        string path = NewPath("mlt_generations");
        using var directory = new MMapDirectory(path);
        Write(directory, 0, DocumentCount, segments: 2);
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

    private static int DocId(IndexSearcher searcher, int id)
        => searcher.Search(new TermQuery("id", id.ToString(CultureInfo.InvariantCulture)), 1, TestContext.Current.CancellationToken).ScoreDocs[0].DocId;

    private MMapDirectory Build(string name, int segments)
    {
        var directory = new MMapDirectory(NewPath(name));
        Write(directory, 0, DocumentCount, segments);
        return directory;
    }

    private static void Write(MMapDirectory directory, int start, int count, int segments)
    {
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = (int)Math.Ceiling(count / (double)segments),
            MergePolicy = NoMergePolicy.Instance,
            StoreTermVectors = true
        });
        for (int i = start; i < start + count; i++)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", i.ToString(CultureInfo.InvariantCulture), stored: true));
            document.Add(new TextField("title", Text(i, 3, 3)));
            document.Add(new TextField("body", Text(i, 12, 7)));
            writer.AddDocument(document);
        }
        writer.Commit();
    }

    // Deterministic text: a skewed choice of words with repeats, so frequencies and document
    // frequencies vary widely.
    private static string Text(int i, int length, int salt)
    {
        var words = new string[length];
        for (int w = 0; w < length; w++)
        {
            int pick = (int)((((long)(i + 1) * 2654435761L) + (w * 40503L) + salt) % 997);
            words[w] = Words[(pick * pick) % Words.Length];
        }
        return string.Join(' ', words);
    }

    private string NewPath(string name)
    {
        string path = Path.Combine(_fixture.Path, name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

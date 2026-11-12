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
/// MoreLikeThis results must not depend on cache warmth: a warm search runs exactly the query a
/// cold one does.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class MoreLikeThisCacheTests : IClassFixture<TestDirectoryFixture>
{
    private const int DocumentCount = 400;
    private static readonly string[] Words =
    [
        "market", "government", "economy", "reform", "policy", "growth", "trade", "budget", "tax", "bank",
        "export", "import", "labour", "wages", "energy", "oil", "climate", "science", "school", "health",
        "court", "law", "vote", "party", "minister", "council", "city", "rural", "farm", "harvest"
    ];

    private readonly TestDirectoryFixture _fixture;

    public MoreLikeThisCacheTests(TestDirectoryFixture fixture) => _fixture = fixture;

    public static TheoryData<int, int, int, int, int, bool> Parameters => new()
    {
        // MaxQueryTerms, MinTermFreq, MinDocFreq, MaxDocFreq, MinWordLength, BoostByScore
        { 25, 1, 1, int.MaxValue, 3, true },
        { 5, 1, 1, int.MaxValue, 3, true },
        { 5, 1, 1, int.MaxValue, 3, false },
        { 8, 2, 1, int.MaxValue, 3, true },
        { 10, 1, 5, int.MaxValue, 4, true },
        { 10, 1, 1, 120, 3, true },
        { 3, 1, 2, 200, 5, false }
    };

    [Theory(DisplayName = "MoreLikeThis: Cold And Warm Searches Are Identical")]
    [MemberData(nameof(Parameters))]
    public void MoreLikeThis_ColdAndWarm_AreIdentical(
        int maxQueryTerms, int minTermFreq, int minDocFreq, int maxDocFreq, int minWordLength, bool boostByScore)
    {
        var parameters = new MoreLikeThisParameters
        {
            MaxQueryTerms = maxQueryTerms,
            MinTermFreq = minTermFreq,
            MinDocFreq = minDocFreq,
            MaxDocFreq = maxDocFreq,
            MinWordLength = minWordLength,
            BoostByScore = boostByScore
        };
        using var directory = Build("mlt_cold_warm", segments: 4);
        using var searcher = new IndexSearcher(directory);
        foreach (int source in new[] { 3, 77, 250 })
        {
            int docId = DocId(searcher, source);
            var query = new MoreLikeThisQuery(docId, ["body", "title"], parameters);
            var extracted = searcher.ExtractMoreLikeThisTerms(query);

            var cold = searcher.MoreLikeThis(docId, ["body", "title"], 20, parameters);
            Assert.True(searcher.TryGetCachedMoreLikeThisTerms(query, out var cached));
            Assert.Equal(extracted, cached);
            var warm = searcher.MoreLikeThis(docId, ["body", "title"], 20, parameters);

            Assert.Equal(cold.TotalHits, warm.TotalHits);
            Assert.Equal(cold.ScoreDocs.Select(static h => h.DocId), warm.ScoreDocs.Select(static h => h.DocId));
            for (int i = 0; i < cold.ScoreDocs.Length; i++)
                Assert.Equal(cold.ScoreDocs[i].Score, warm.ScoreDocs[i].Score, 5);
        }
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

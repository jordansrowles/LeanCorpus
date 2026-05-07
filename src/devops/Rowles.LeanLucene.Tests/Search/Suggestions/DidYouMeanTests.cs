using System.Diagnostics;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Search.Searcher;
using Rowles.LeanLucene.Search.Suggestions;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Search.Suggestions;

/// <summary>
/// Contains unit tests for Did You Mean.
/// </summary>
public sealed class DidYouMeanTests : IClassFixture<TestDirectoryFixture>
{
    private readonly string _path;

    public DidYouMeanTests(TestDirectoryFixture fixture) => _path = fixture.Path;

    private string SubDir(string name)
    {
        var dir = Path.Combine(_path, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Verifies the Suggest: Returns Closest Term By Edit Distance scenario.
    /// </summary>
    [Fact(DisplayName = "Suggest: Returns Closest Term By Edit Distance")]
    public void Suggest_ReturnsClosestTermByEditDistance()
    {
        var mmap = new MMapDirectory(SubDir(nameof(Suggest_ReturnsClosestTermByEditDistance)));

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            foreach (var word in new[] { "hello", "help", "hero", "world" })
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", word));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var suggestions = DidYouMeanSuggester.Suggest(searcher, "body", "helo", maxEdits: 2, topN: 5);

        Assert.NotEmpty(suggestions);
        Assert.Contains(suggestions, s => s.Term == "hello");
    }

    /// <summary>
    /// Verifies the Suggest: Excludes Exact Match scenario.
    /// </summary>
    [Fact(DisplayName = "Suggest: Excludes Exact Match")]
    public void Suggest_ExcludesExactMatch()
    {
        var mmap = new MMapDirectory(SubDir(nameof(Suggest_ExcludesExactMatch)));

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var suggestions = DidYouMeanSuggester.Suggest(searcher, "body", "hello");

        Assert.DoesNotContain(suggestions, s => s.Term == "hello");
    }

    /// <summary>
    /// Verifies the Suggest: Respects Top N scenario.
    /// </summary>
    [Fact(DisplayName = "Suggest: Respects Top N")]
    public void Suggest_RespectsTopN()
    {
        var mmap = new MMapDirectory(SubDir(nameof(Suggest_RespectsTopN)));

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            foreach (var word in new[] { "cat", "car", "cap", "cab", "can", "cam" })
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", word));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var suggestions = DidYouMeanSuggester.Suggest(searcher, "body", "cas", topN: 3);

        Assert.True(suggestions.Count <= 3);
    }

    /// <summary>
    /// Verifies the Suggest: Returns Empty For No Matches scenario.
    /// </summary>
    [Fact(DisplayName = "Suggest: Returns Empty For No Matches")]
    public void Suggest_ReturnsEmptyForNoMatches()
    {
        var mmap = new MMapDirectory(SubDir(nameof(Suggest_ReturnsEmptyForNoMatches)));

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var suggestions = DidYouMeanSuggester.Suggest(searcher, "body", "zzzzz", maxEdits: 1);

        Assert.Empty(suggestions);
    }

    // ── New game-plan tests ─────────────────────────────────────────────

    /// <summary>
    /// Verifies the Suggest: Search Typo Inserted Letter scenario.
    /// </summary>
    [Fact(DisplayName = "Suggest: Search Typo Inserted Letter")]
    public void Suggest_SearchTypo_InsertedLetter()
    {
        // "serch" → suggests "search" (edit distance 1: insert 'a')
        var mmap = new MMapDirectory(SubDir(nameof(Suggest_SearchTypo_InsertedLetter)));

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            foreach (var word in new[] { "search", "research", "scratch", "season" })
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", word));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var suggestions = DidYouMeanSuggester.Suggest(searcher, "body", "serch", maxEdits: 2);

        Assert.NotEmpty(suggestions);
        Assert.Contains(suggestions, s => s.Term == "search");
    }

    /// <summary>
    /// Verifies the Suggest: Vector Typo Inserted Letter scenario.
    /// </summary>
    [Fact(DisplayName = "Suggest: Vector Typo Inserted Letter")]
    public void Suggest_VectorTypo_InsertedLetter()
    {
        // "vectr" → suggests "vector" (edit distance 1: insert 'o')
        var mmap = new MMapDirectory(SubDir(nameof(Suggest_VectorTypo_InsertedLetter)));

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            foreach (var word in new[] { "vector", "sector", "factor", "victor" })
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", word));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var suggestions = DidYouMeanSuggester.Suggest(searcher, "body", "vectr", maxEdits: 2);

        Assert.NotEmpty(suggestions);
        Assert.Contains(suggestions, s => s.Term == "vector");
    }

    /// <summary>
    /// Verifies the Suggest: Language Typo Edit Distance 2 scenario.
    /// </summary>
    [Fact(DisplayName = "Suggest: Language Typo Edit Distance 2")]
    public void Suggest_LanguageTypo_EditDistance2()
    {
        // "languge" → suggests "language" (edit distance 2)
        var mmap = new MMapDirectory(SubDir(nameof(Suggest_LanguageTypo_EditDistance2)));

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            foreach (var word in new[] { "language", "luggage", "passage", "sausage" })
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", word));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var suggestions = DidYouMeanSuggester.Suggest(searcher, "body", "languge", maxEdits: 2);

        Assert.NotEmpty(suggestions);
        Assert.Contains(suggestions, s => s.Term == "language");
    }

    /// <summary>
    /// Verifies the Suggest: Gibberish Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Suggest: Gibberish Returns Empty")]
    public void Suggest_Gibberish_ReturnsEmpty()
    {
        var mmap = new MMapDirectory(SubDir(nameof(Suggest_Gibberish_ReturnsEmpty)));

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            foreach (var word in new[] { "search", "vector", "language" })
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", word));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var suggestions = DidYouMeanSuggester.Suggest(searcher, "body", "xyzqwkj", maxEdits: 2);

        Assert.Empty(suggestions);
    }

    /// <summary>
    /// Verifies the Suggest: Higher Doc Frequency Ranks Higher scenario.
    /// </summary>
    [Fact(DisplayName = "Suggest: Higher Doc Frequency Ranks Higher")]
    public void Suggest_HigherDocFrequency_RanksHigher()
    {
        // "helt" is edit distance 1 from both "help" and "held"
        // "help" appears in 5 docs, "held" in 1 → "help" should rank higher
        var mmap = new MMapDirectory(SubDir(nameof(Suggest_HigherDocFrequency_RanksHigher)));

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            for (int i = 0; i < 5; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", "help"));
                writer.AddDocument(doc);
            }
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", "held"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var suggestions = DidYouMeanSuggester.Suggest(searcher, "body", "helt", maxEdits: 1);

        Assert.True(suggestions.Count >= 2);
        Assert.Equal("help", suggestions[0].Term);
    }
}

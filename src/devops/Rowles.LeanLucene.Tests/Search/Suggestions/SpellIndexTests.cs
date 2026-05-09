using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Search.Searcher;
using Rowles.LeanLucene.Search.Suggestions;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Search.Suggestions;

/// <summary>
/// Contains unit tests for Spell Index.
/// </summary>
public sealed class SpellIndexTests : IClassFixture<TestDirectoryFixture>
{
    private readonly string _path;

    public SpellIndexTests(TestDirectoryFixture fixture) => _path = fixture.Path;

    private string SubDir(string name)
    {
        var dir = Path.Combine(_path, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ── Integration tests (via IndexSearcher) ───────────────────────────

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
        var spellIndex = SpellIndex.Build(searcher, "body");
        var suggestions = spellIndex.Suggest("helo", maxEdits: 2, topN: 5);

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
        var spellIndex = SpellIndex.Build(searcher, "body");
        var suggestions = spellIndex.Suggest("hello");

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
        var spellIndex = SpellIndex.Build(searcher, "body");
        var suggestions = spellIndex.Suggest("cas", topN: 3);

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
        var spellIndex = SpellIndex.Build(searcher, "body");
        var suggestions = spellIndex.Suggest("zzzzz", maxEdits: 1);

        Assert.Empty(suggestions);
    }

    /// <summary>
    /// Verifies the Suggest: Higher Doc Frequency Ranks Higher scenario.
    /// </summary>
    [Fact(DisplayName = "Suggest: Higher Doc Frequency Ranks Higher")]
    public void Suggest_HigherDocFrequency_RanksHigher()
    {
        // "helt" is edit distance 1 from both "help" and "held"
        // "help" appears in 5 docs, "held" in 1 -- "help" should rank higher
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
        var spellIndex = SpellIndex.Build(searcher, "body");
        var suggestions = spellIndex.Suggest("helt", maxEdits: 1);

        Assert.True(suggestions.Count >= 2);
        Assert.Equal("help", suggestions[0].Term);
    }

    /// <summary>
    /// Verifies the Suggest: Search Typo Inserted Letter scenario.
    /// </summary>
    [Fact(DisplayName = "Suggest: Search Typo Inserted Letter")]
    public void Suggest_SearchTypo_InsertedLetter()
    {
        // "serch" should suggest "search" (edit distance 1: insert 'a')
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
        var spellIndex = SpellIndex.Build(searcher, "body");
        var suggestions = spellIndex.Suggest("serch", maxEdits: 2);

        Assert.NotEmpty(suggestions);
        Assert.Contains(suggestions, s => s.Term == "search");
    }

    // ── Unit tests via BuildFromTerms (no I/O) ─────────────────────────

    /// <summary>
    /// Verifies the Build From Terms: Handles Short Terms scenario.
    /// </summary>
    [Fact(DisplayName = "Build From Terms: Handles Short Terms")]
    public void BuildFromTerms_HandlesShortTerms()
    {
        // Terms shorter than 3 chars produce no trigrams but should still
        // be found via the short-query brute-force fallback path.
        var index = SpellIndex.BuildFromTerms([
            ("go", 10),
            ("do", 5),
            ("no", 3),
            ("so", 8)
        ]);

        var suggestions = index.Suggest("bo", maxEdits: 1, topN: 5);

        Assert.NotEmpty(suggestions);
        Assert.All(suggestions, s => Assert.True(s.EditDistance <= 1));
    }

    /// <summary>
    /// Verifies the Build From Terms: Empty Index Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Build From Terms: Empty Index Returns Empty")]
    public void BuildFromTerms_EmptyIndex_ReturnsEmpty()
    {
        var index = SpellIndex.BuildFromTerms([]);
        var suggestions = index.Suggest("hello");
        Assert.Empty(suggestions);
    }

    /// <summary>
    /// Verifies the Build From Terms: Term Count Matches Input scenario.
    /// </summary>
    [Fact(DisplayName = "Build From Terms: Term Count Matches Input")]
    public void BuildFromTerms_TermCount_MatchesInput()
    {
        var index = SpellIndex.BuildFromTerms([
            ("alpha", 1),
            ("bravo", 1),
            ("charlie", 1)
        ]);

        Assert.Equal(3, index.TermCount);
    }

    /// <summary>
    /// Verifies the Build From Terms: Edit Distance 2 Finds Match scenario.
    /// </summary>
    [Fact(DisplayName = "Build From Terms: Edit Distance 2 Finds Match")]
    public void BuildFromTerms_EditDistance2_FindsMatch()
    {
        // "languge" should suggest "language" (edit distance 2)
        var index = SpellIndex.BuildFromTerms([
            ("language", 10),
            ("luggage", 5),
            ("passage", 3),
            ("sausage", 1)
        ]);

        var suggestions = index.Suggest("languge", maxEdits: 2);

        Assert.NotEmpty(suggestions);
        Assert.Contains(suggestions, s => s.Term == "language");
    }

    /// <summary>
    /// Verifies the Build From Terms: Gibberish Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Build From Terms: Gibberish Returns Empty")]
    public void BuildFromTerms_Gibberish_ReturnsEmpty()
    {
        var index = SpellIndex.BuildFromTerms([
            ("search", 10),
            ("vector", 5),
            ("language", 3)
        ]);

        var suggestions = index.Suggest("xyzqwkj", maxEdits: 2);

        Assert.Empty(suggestions);
    }
}

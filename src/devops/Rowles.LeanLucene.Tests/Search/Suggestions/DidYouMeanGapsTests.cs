using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Search.Suggestions;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Search.Suggestions;

/// <summary>
/// Tests covering the DidYouMeanSuggester argument-validation paths and the
/// second Suggest overload (SpellIndex-direct), including the SpellIndex.BuildFromTerms
/// internal factory (accessible via InternalsVisibleTo).
/// </summary>
public sealed class DidYouMeanGapsTests : IDisposable
{
    private readonly string _dir;

    public DidYouMeanGapsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_dym_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private IndexSearcher BuildSearcher()
    {
        var mmap = new MMapDirectory(_dir);
        using var writer = new IndexWriter(mmap, new IndexWriterConfig());
        foreach (var word in new[] { "search", "searching", "searcher" })
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("word", word));
            writer.AddDocument(doc);
        }
        writer.Commit();
        return new IndexSearcher(mmap);
    }

    // ── First overload argument validation ────────────────────────────────────

    /// <summary>Verifies the first Suggest overload throws for null searcher.</summary>
    [Fact(DisplayName = "DidYouMeanSuggester: Null Searcher Throws")]
    public void DidYouMeanSuggester_NullSearcher_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DidYouMeanSuggester.Suggest(null!, "word", "query"));
    }

    /// <summary>Verifies the first Suggest overload throws for null field.</summary>
    [Fact(DisplayName = "DidYouMeanSuggester: Null Field Throws")]
    public void DidYouMeanSuggester_NullField_Throws()
    {
        using var searcher = BuildSearcher();
        Assert.Throws<ArgumentNullException>(() =>
            DidYouMeanSuggester.Suggest(searcher, null!, "query"));
    }

    /// <summary>Verifies the first Suggest overload throws for empty field.</summary>
    [Fact(DisplayName = "DidYouMeanSuggester: Empty Field Throws")]
    public void DidYouMeanSuggester_EmptyField_Throws()
    {
        using var searcher = BuildSearcher();
        Assert.Throws<ArgumentException>(() =>
            DidYouMeanSuggester.Suggest(searcher, string.Empty, "query"));
    }

    /// <summary>Verifies the first Suggest overload throws for null query term.</summary>
    [Fact(DisplayName = "DidYouMeanSuggester: Null QueryTerm Throws")]
    public void DidYouMeanSuggester_NullQueryTerm_Throws()
    {
        using var searcher = BuildSearcher();
        Assert.Throws<ArgumentNullException>(() =>
            DidYouMeanSuggester.Suggest(searcher, "word", null!));
    }

    /// <summary>Verifies the first Suggest overload throws when topN is zero.</summary>
    [Fact(DisplayName = "DidYouMeanSuggester: Zero TopN Throws")]
    public void DidYouMeanSuggester_ZeroTopN_Throws()
    {
        using var searcher = BuildSearcher();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DidYouMeanSuggester.Suggest(searcher, "word", "search", topN: 0));
    }

    // ── Second overload (SpellIndex direct) ───────────────────────────────────

    /// <summary>Verifies the second Suggest overload throws for null index.</summary>
    [Fact(DisplayName = "DidYouMeanSuggester: Null SpellIndex Throws")]
    public void DidYouMeanSuggester_NullSpellIndex_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DidYouMeanSuggester.Suggest((SpellIndex)null!, "query"));
    }

    /// <summary>Verifies the second Suggest overload throws for null query term.</summary>
    [Fact(DisplayName = "DidYouMeanSuggester: SpellIndex Null QueryTerm Throws")]
    public void DidYouMeanSuggester_SpellIndex_NullQueryTerm_Throws()
    {
        var index = SpellIndex.BuildFromTerms([("hello", 1)]);
        Assert.Throws<ArgumentNullException>(() =>
            DidYouMeanSuggester.Suggest(index, null!));
    }

    /// <summary>Verifies the second Suggest overload throws when topN is zero.</summary>
    [Fact(DisplayName = "DidYouMeanSuggester: SpellIndex Zero TopN Throws")]
    public void DidYouMeanSuggester_SpellIndex_ZeroTopN_Throws()
    {
        var index = SpellIndex.BuildFromTerms([("hello", 1)]);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DidYouMeanSuggester.Suggest(index, "hello", topN: 0));
    }

    /// <summary>Verifies the second Suggest overload returns results for a valid SpellIndex.</summary>
    [Fact(DisplayName = "DidYouMeanSuggester: SpellIndex Returns Corrections")]
    public void DidYouMeanSuggester_SpellIndex_ReturnsCorrections()
    {
        var index = SpellIndex.BuildFromTerms([
            ("searching", 3),
            ("searcher", 2),
            ("searches", 1)
        ]);
        var suggestions = DidYouMeanSuggester.Suggest(index, "search");
        Assert.NotEmpty(suggestions);
    }
}

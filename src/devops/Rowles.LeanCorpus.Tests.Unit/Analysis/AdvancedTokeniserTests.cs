using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

[Trait("Category", "Analysis")]
public sealed class AdvancedTokeniserTests
{
    [Fact(DisplayName = "ICU Tokeniser: Unicode Terms Preserve Offsets")]
    public void IcuTokeniser_UnicodeTerms_PreserveOffsets()
    {
        var tokeniser = new IcuTokeniser();

        var tokens = tokeniser.Tokenise("Straße café ภาษาไทย");

        // Without Thai injection, Thai chars are treated as regular word characters
        Assert.Equal(["Straße", "café", "ภาษาไทย"], tokens.Select(static token => token.Text));
        Assert.Equal([0, 7, 12], tokens.Select(static token => token.StartOffset));
        Assert.Equal([6, 11, 19], tokens.Select(static token => token.EndOffset));
    }

    [Fact(DisplayName = "ICU Tokeniser: With Thai Tokeniser Segments Thai Properly")]
    public void IcuTokeniser_WithThai_SegmentsThai()
    {
        var thai = ThaiTokeniser.FromFile(ResolveLexiconPath("thai-dict.txt"));
        var tokeniser = new IcuTokeniser(thai);

        var tokens = tokeniser.Tokenise("Straße café ภาษาไทย");

        Assert.Equal(["Straße", "café", "ภาษา", "ไทย"], tokens.Select(static token => token.Text));
        Assert.Equal([0, 7, 12, 16], tokens.Select(static token => token.StartOffset));
        Assert.Equal([6, 11, 16, 19], tokens.Select(static token => token.EndOffset));
    }

    [Fact(DisplayName = "UAX29 URL Email Tokeniser: Preserves Special Token Types")]
    public void Uax29UrlEmailTokeniser_PreservesSpecialTokenTypes()
    {
        var tokeniser = new Uax29UrlEmailTokeniser();

        var tokens = tokeniser.Tokenise("Mail dev@example.com https://example.com/docs #LeanCorpus @jordansrowles");

        Assert.Contains(tokens, static token => token.Text == "dev@example.com" && token.Type == Uax29UrlEmailTokeniser.EmailType);
        Assert.Contains(tokens, static token => token.Text == "https://example.com/docs" && token.Type == Uax29UrlEmailTokeniser.UrlType);
        Assert.Contains(tokens, static token => token.Text == "#LeanCorpus" && token.Type == Uax29UrlEmailTokeniser.HashtagType);
        Assert.Contains(tokens, static token => token.Text == "@jordansrowles" && token.Type == Uax29UrlEmailTokeniser.MentionType);
    }

    [Fact(DisplayName = "Thai Tokeniser: FromFile Loads Lexicon and Splits Known Runs")]
    public void ThaiTokeniser_FromFile_SplitsKnownRuns()
    {
        var tokeniser = ThaiTokeniser.FromFile(ResolveLexiconPath("thai-dict.txt"));

        var tokens = tokeniser.Tokenise("ยินดีต้อนรับภาษาไทย");

        Assert.Equal(["ยินดี", "ต้อนรับ", "ภาษา", "ไทย"], tokens.Select(static token => token.Text));
        Assert.All(tokens, static token => Assert.Equal(ThaiTokeniser.ThaiType, token.Type));
    }

    [Fact(DisplayName = "Thai Tokeniser: Unknown Words Fall Back to Grapheme Clusters")]
    public void ThaiTokeniser_UnknownWords_FallsBackToClusters()
    {
        var tokeniser = ThaiTokeniser.FromFile(ResolveLexiconPath("thai-dict.txt"));

        // "กขคง" is not in the lexicon, should fall back to clusters
        var tokens = tokeniser.Tokenise("กขคง");

        Assert.NotEmpty(tokens);
        Assert.All(tokens, static token => Assert.Equal(ThaiTokeniser.ThaiType, token.Type));
    }

    [Fact(DisplayName = "Thai Tokeniser: Offers Offset Tracking")]
    public void ThaiTokeniser_Offsets_AreCorrect()
    {
        var tokeniser = ThaiTokeniser.FromFile(ResolveLexiconPath("thai-dict.txt"));

        var tokens = tokeniser.Tokenise("สวัสดี โลก");

        Assert.Equal([0, 7], tokens.Select(static token => token.StartOffset));
        Assert.Equal([6, 10], tokens.Select(static token => token.EndOffset));
    }

    [Fact(DisplayName = "MediaWiki Tokeniser: Emits Typed Markup Tokens")]
    public void MediaWikiTokeniser_EmitsTypedMarkupTokens()
    {
        var tokeniser = new MediaWikiTokeniser();

        var tokens = tokeniser.Tokenise("[[Category:Search Engines]] [[Main Page|Lean Corpus]] '''Bold''' ''Italic'' <ref>Citation note</ref>");

        Assert.Contains(tokens, static token => token.Text == "Search" && token.Type == MediaWikiTokeniser.CategoryType);
        Assert.Contains(tokens, static token => token.Text == "Lean" && token.Type == MediaWikiTokeniser.InternalLinkType);
        Assert.Contains(tokens, static token => token.Text == "Bold" && token.Type == MediaWikiTokeniser.BoldType);
        Assert.Contains(tokens, static token => token.Text == "Italic" && token.Type == MediaWikiTokeniser.ItalicType);
        Assert.Contains(tokens, static token => token.Text == "Citation" && token.Type == MediaWikiTokeniser.CitationType);
    }

    [Fact(DisplayName = "ICU Analyser: Lowercases And Removes Stop Words")]
    public void IcuAnalyser_LowercasesAndRemovesStopWords()
    {
        var analyser = new IcuAnalyser();

        var tokens = analyser.Analyse("The Café and Straße");

        Assert.Equal(["café", "straße"], tokens.Select(static token => token.Text));
    }

    [Fact(DisplayName = "ICU Analyser: With Thai Tokeniser Segments Thai")]
    public void IcuAnalyser_WithThai_SegmentsThai()
    {
        var thai = ThaiTokeniser.FromFile(ResolveLexiconPath("thai-dict.txt"));
        var analyser = new IcuAnalyser(thaiTokeniser: thai);

        var tokens = analyser.Analyse("The ภาษาไทย");

        Assert.Contains("ภาษา", tokens.Select(static token => token.Text));
        Assert.Contains("ไทย", tokens.Select(static token => token.Text));
    }

    [Fact(DisplayName = "Thai Tokeniser: Null Lexicon Throws")]
    public void ThaiTokeniser_NullLexicon_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ThaiTokeniser(null!));

    [Fact(DisplayName = "Thai Tokeniser: Empty Lexicon Throws")]
    public void ThaiTokeniser_EmptyLexicon_Throws()
        => Assert.Throws<ArgumentException>(() => new ThaiTokeniser([]));

    private static string ResolveLexiconPath(string fileName)
    {
        // Walk up from the test output directory to the repo root
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "lexicons", fileName)))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException($"Could not find lexicons/{fileName}. Ensure the test is run from within the repository.");
        return Path.Combine(dir.FullName, "lexicons", fileName);
    }
}

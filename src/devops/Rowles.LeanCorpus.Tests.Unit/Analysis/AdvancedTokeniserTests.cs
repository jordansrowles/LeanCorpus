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

    [Fact(DisplayName = "Thai Tokeniser: Greedy Lexicon Splits Known Runs")]
    public void ThaiTokeniser_GreedyLexicon_SplitsKnownRuns()
    {
        var tokeniser = new ThaiTokeniser();

        var tokens = tokeniser.Tokenise("ยินดีต้อนรับภาษาไทย");

        Assert.Equal(["ยินดี", "ต้อนรับ", "ภาษา", "ไทย"], tokens.Select(static token => token.Text));
        Assert.All(tokens, static token => Assert.Equal(ThaiTokeniser.ThaiType, token.Type));
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
}

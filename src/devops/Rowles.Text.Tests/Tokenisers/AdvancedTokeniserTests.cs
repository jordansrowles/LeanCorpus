using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.Text.Tests;
[Category(TestCategory.Unit)]
[Area(TestArea.Tokenisers)]
public sealed class AdvancedTokeniserTests
{
    [Fact(DisplayName = "URL/email tokeniser: legacy UAX #29 name advertises its compatibility status")]
    public void Uax29UrlEmailTokeniser_IsObsoleteCompatibilityWrapper()
    {
#pragma warning disable CS0618
        Type legacyType = typeof(Uax29UrlEmailTokeniser);
#pragma warning restore CS0618
        var obsolete = (ObsoleteAttribute?)Attribute.GetCustomAttribute(legacyType, typeof(ObsoleteAttribute));

        Assert.NotNull(obsolete);
        Assert.False(obsolete!.IsError);
        Assert.Contains("UrlEmailTokeniser", obsolete.Message, StringComparison.Ordinal);
        Assert.Contains("does not implement UAX #29", obsolete.Message, StringComparison.Ordinal);
        Assert.NotNull(legacyType.Assembly.GetType("Rowles.LeanCorpus.Analysis.Tokenisers.UrlEmailTokeniser"));
    }

    [Fact(DisplayName = "ICU Tokeniser: Unicode Terms Preserve Offsets")]
    public void IcuTokeniser_UnicodeTerms_PreserveOffsets()
    {
        var tokeniser = new IcuTokeniser();

        var matSink = new MaterialisingTokenSink();
        tokeniser.Tokenise("Straße café ภาษาไทย", matSink);
        var tokens = matSink.Tokens;

        // Without Thai injection, Thai chars are treated as regular word characters
        Assert.Equal(["Straße", "café", "ภาษาไทย"], tokens.Select(static token => token.Text));
        Assert.Equal([0, 7, 12], tokens.Select(static token => token.StartOffset));
        Assert.Equal([6, 11, 19], tokens.Select(static token => token.EndOffset));
    }

    [Fact(DisplayName = "ICU Tokeniser: Supplementary Letters Digits And Marks Use Scalar Boundaries")]
    public void IcuTokeniser_SupplementaryLettersDigitsAndMarks_UsesScalarBoundaries()
    {
        const string input = "A\U00010400B \U0001D7D8 a\U0001D165b";
        var tokeniser = new IcuTokeniser();

        var matSink = new MaterialisingTokenSink();
        tokeniser.Tokenise(input, matSink);
        var tokens = matSink.Tokens;

        Assert.Equal(["A\U00010400B", "\U0001D7D8", "a\U0001D165b"], tokens.Select(static token => token.Text));
        Assert.Equal([0, 5, 8], tokens.Select(static token => token.StartOffset));
        Assert.Equal([4, 7, 12], tokens.Select(static token => token.EndOffset));
        Assert.Equal([Token.DefaultType, "number", Token.DefaultType], tokens.Select(static token => token.Type));
    }

    [Fact(DisplayName = "ICU Tokeniser: With Thai Tokeniser Segments Thai Properly")]
    public void IcuTokeniser_WithThai_SegmentsThai()
    {
        var thai = ThaiTokeniser.FromFile(ResolveLexiconPath("thai-dict.txt"));
        var tokeniser = new IcuTokeniser(thai);

        var matSink = new MaterialisingTokenSink();
        tokeniser.Tokenise("Straße café ภาษาไทย", matSink);
        var tokens = matSink.Tokens;

        Assert.Equal(["Straße", "café", "ภาษา", "ไทย"], tokens.Select(static token => token.Text));
        Assert.Equal([0, 7, 12, 16], tokens.Select(static token => token.StartOffset));
        Assert.Equal([6, 11, 16, 19], tokens.Select(static token => token.EndOffset));
    }

    [Fact(DisplayName = "URL/email tokeniser: Preserves Heuristic Token Types")]
    public void UrlEmailTokeniser_PreservesHeuristicTokenTypes()
    {
        var tokeniser = new UrlEmailTokeniser();

        var matSink = new MaterialisingTokenSink();
        tokeniser.Tokenise("Mail dev@example.com https://example.com/docs #LeanCorpus @jordansrowles", matSink);
        var tokens = matSink.Tokens;

        Assert.Equal(
        [
            ("Mail", 0, 4, Token.DefaultType, 1, 1),
            ("dev@example.com", 5, 20, UrlEmailTokeniser.EmailType, 1, 1),
            ("https://example.com/docs", 21, 45, UrlEmailTokeniser.UrlType, 1, 1),
            ("#LeanCorpus", 46, 57, UrlEmailTokeniser.HashtagType, 1, 1),
            ("@jordansrowles", 58, 72, UrlEmailTokeniser.MentionType, 1, 1)
        ],
            tokens.Select(static token =>
                (token.Text, token.StartOffset, token.EndOffset, token.Type, token.PositionIncrement, token.PositionLength)));
    }

    [Fact(DisplayName = "URL/email tokeniser: Supplementary Words Stay In Hashtags And Emails")]
    public void UrlEmailTokeniser_SupplementaryWords_StaysInHashtagsAndEmails()
    {
        const string tag = "#A\U00010400B";
        const string email = "dev\U00010400@example.com";
        string input = $"{tag} {email}";
        var tokeniser = new UrlEmailTokeniser();

        var matSink = new MaterialisingTokenSink();
        tokeniser.Tokenise(input, matSink);
        var tokens = matSink.Tokens;

        Assert.Equal(2, tokens.Count);
        Assert.Equal(tag, tokens[0].Text);
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal(tag.Length, tokens[0].EndOffset);
        Assert.Equal(UrlEmailTokeniser.HashtagType, tokens[0].Type);
        Assert.Equal(email, tokens[1].Text);
        Assert.Equal(tag.Length + 1, tokens[1].StartOffset);
        Assert.Equal(input.Length, tokens[1].EndOffset);
        Assert.Equal(UrlEmailTokeniser.EmailType, tokens[1].Type);
    }

    [Fact(DisplayName = "URL/email tokeniser: Legacy wrapper forwards equivalent tokens and thread-local instances")]
    public void Uax29UrlEmailTokeniser_ForwardsToUrlEmailTokeniser()
    {
#pragma warning disable CS0618
        var legacy = new Uax29UrlEmailTokeniser();
        ISpanTokeniser threadLocalLegacy = legacy.CreateThreadLocalTokeniser();
#pragma warning restore CS0618
        const string input = "Mail dev@example.com https://example.com/docs #LeanCorpus @jordansrowles";

        static (string Text, int StartOffset, int EndOffset, string Type, int PositionIncrement, int PositionLength)[]
            Capture(ISpanTokeniser tokeniser, string value)
        {
            var sink = new MaterialisingTokenSink();
            tokeniser.Tokenise(value, sink);
            return sink.Tokens.Select(static token =>
                (token.Text, token.StartOffset, token.EndOffset, token.Type, token.PositionIncrement, token.PositionLength)).ToArray();
        }

        var expected = Capture(new UrlEmailTokeniser(), input);
        Assert.Equal(expected, Capture(legacy, input));
        Assert.Equal(expected, Capture(threadLocalLegacy, input));

        const string thaiInput = "ภาษาไทย dev@example.com";
        var expectedThai = Capture(new UrlEmailTokeniser(new ThaiTokeniser(["ภาษา", "ไทย"])), thaiInput);
#pragma warning disable CS0618
        var legacyThai = new Uax29UrlEmailTokeniser(new ThaiTokeniser(["ภาษา", "ไทย"]));
        ISpanTokeniser threadLocalLegacyThai = legacyThai.CreateThreadLocalTokeniser();
#pragma warning restore CS0618

        Assert.Equal(["ภาษา", "ไทย", "dev@example.com"], expectedThai.Select(static token => token.Text));
        Assert.Equal(expectedThai, Capture(legacyThai, thaiInput));
        Assert.Equal(expectedThai, Capture(threadLocalLegacyThai, thaiInput));
    }

    [Fact(DisplayName = "Thai Tokeniser: Supplementary Non-Thai Letters Use Scalar Boundaries")]
    public void ThaiTokeniser_SupplementaryNonThaiLetters_UsesScalarBoundaries()
    {
        const string input = "A\U00010400B";
        var tokeniser = new ThaiTokeniser(["ภาษา"]);

        var matSink = new MaterialisingTokenSink();
        tokeniser.Tokenise(input, matSink);

        var token = Assert.Single(matSink.Tokens);
        Assert.Equal(input, token.Text);
        Assert.Equal(0, token.StartOffset);
        Assert.Equal(input.Length, token.EndOffset);
    }

    [Fact(DisplayName = "Thai Tokeniser: FromFile Loads Lexicon and Splits Known Runs")]
    public void ThaiTokeniser_FromFile_SplitsKnownRuns()
    {
        var tokeniser = ThaiTokeniser.FromFile(ResolveLexiconPath("thai-dict.txt"));

        var matSink = new MaterialisingTokenSink();
        tokeniser.Tokenise("ยินดีต้อนรับภาษาไทย", matSink);
        var tokens = matSink.Tokens;

        Assert.Equal(["ยินดี", "ต้อนรับ", "ภาษา", "ไทย"], tokens.Select(static token => token.Text));
        Assert.All(tokens, static token => Assert.Equal(ThaiTokeniser.ThaiType, token.Type));
    }

    [Fact(DisplayName = "Lexicon tokenisers: Thai and Chinese share one longest-prefix trie")]
    public void LexiconTokenisers_UseTriePrefixLookup()
    {
        AssertTrieBacked(typeof(ThaiTokeniser));
        AssertTrieBacked(typeof(ChineseLexiconTokeniser));

        static void AssertTrieBacked(Type tokeniserType)
        {
            var matcher = tokeniserType.GetField(
                "_prefixTrie",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.NotNull(matcher);
            Assert.Equal("LexiconPrefixTrie", matcher!.FieldType.Name);
        }
    }

    [Fact(DisplayName = "Thai Tokeniser: Longest prefix and cluster fallback preserve token contract")]
    public void ThaiTokeniser_LongestPrefixAndFallback_PreservesTokenContract()
    {
        var tokeniser = new ThaiTokeniser(["ก", "กข", "กขค"]);
        var sink = new MaterialisingTokenSink();
        const string input = "กขคง";

        tokeniser.Tokenise(input, sink);

        Assert.Equal(
            new[]
            {
                ("กขค", 0, 3, ThaiTokeniser.ThaiType, 1, 1),
                ("ง", 3, 4, ThaiTokeniser.ThaiType, 1, 1)
            },
            sink.Tokens.Select(static token =>
                (token.Text, token.StartOffset, token.EndOffset, token.Type, token.PositionIncrement, token.PositionLength)));
    }

    [Fact(DisplayName = "Thai Tokeniser: Unknown Words Fall Back to Grapheme Clusters")]
    public void ThaiTokeniser_UnknownWords_FallsBackToClusters()
    {
        var tokeniser = ThaiTokeniser.FromFile(ResolveLexiconPath("thai-dict.txt"));

        // "กขคง" is not in the lexicon, should fall back to clusters
        var matSink = new MaterialisingTokenSink();
        tokeniser.Tokenise("กขคง", matSink);
        var tokens = matSink.Tokens;

        Assert.NotEmpty(tokens);
        Assert.All(tokens, static token => Assert.Equal(ThaiTokeniser.ThaiType, token.Type));
    }

    [Fact(DisplayName = "Thai Tokeniser: Offers Offset Tracking")]
    public void ThaiTokeniser_Offsets_AreCorrect()
    {
        var tokeniser = ThaiTokeniser.FromFile(ResolveLexiconPath("thai-dict.txt"));

        var matSink = new MaterialisingTokenSink();
        tokeniser.Tokenise("สวัสดี โลก", matSink);
        var tokens = matSink.Tokens;

        Assert.Equal([0, 7], tokens.Select(static token => token.StartOffset));
        Assert.Equal([6, 10], tokens.Select(static token => token.EndOffset));
    }

    [Fact(DisplayName = "MediaWiki Tokeniser: Emits Typed Markup Tokens")]
    public void MediaWikiTokeniser_EmitsTypedMarkupTokens()
    {
        var tokeniser = new MediaWikiTokeniser();

        var matSink = new MaterialisingTokenSink();
        tokeniser.Tokenise("[[Category:Search Engines]] [[Main Page|Lean Corpus]] '''Bold''' ''Italic'' <ref>Citation note</ref>", matSink);
        var tokens = matSink.Tokens;

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

        var matSink = new MaterialisingTokenSink();
        analyser.Analyse("The Café and Straße", matSink);
        var tokens = matSink.Tokens;

        Assert.Equal(["café", "straße"], tokens.Select(static token => token.Text));
    }

    [Fact(DisplayName = "ICU Analyser: With Thai Tokeniser Segments Thai")]
    public void IcuAnalyser_WithThai_SegmentsThai()
    {
        var thai = ThaiTokeniser.FromFile(ResolveLexiconPath("thai-dict.txt"));
        var analyser = new IcuAnalyser(thaiTokeniser: thai);

        var matSink = new MaterialisingTokenSink();
        analyser.Analyse("The ภาษาไทย", matSink);
        var tokens = matSink.Tokens;

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

using System.Linq;
using Xunit;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Document.Json;
using Rowles.LeanCorpus.Mapping;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Tests.AOTSmoke;

public class AnalysisSmokeTests
{
    // =========================================================================
    // Tokeniser smoke
    // =========================================================================

    [Fact]
    public void PatternTokeniser_BackslashSPlus()
    {
        var tokeniser = new PatternTokeniser(@"\S+");
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("the quick brown fox", sink);
        Assert.True(sink.Count == 4, $"PatternTokeniser \\S+ expected 4 tokens, got {sink.Count}");
    }

    [Fact]
    public void PatternTokeniser_CompiledRegex()
    {
        var regex = new System.Text.RegularExpressions.Regex(@"[^,]+",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            System.TimeSpan.FromSeconds(1));
        var tokeniser = new PatternTokeniser(regex);
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("alpha,beta,gamma", sink);
        Assert.True(sink.Count == 3, $"PatternTokeniser(Regex) expected 3 tokens, got {sink.Count}");
    }

    [Fact]
    public void CJKBigramTokeniser_ProducesBigrams()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("\u4F60\u597D\u4E16\u754C", sink);
        Assert.True(sink.Count >= 2, $"CJKBigramTokeniser expected >=2 bigrams, got {sink.Count}");
    }

    [Fact]
    public void EdgeNGramTokeniser_ProducesGrams()
    {
        var tokeniser = new EdgeNGramTokeniser(minGram: 2, maxGram: 4);
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("hello", sink);
        Assert.True(sink.Count >= 2, $"EdgeNGramTokeniser expected >=2 grams, got {sink.Count}");
    }

    [Fact]
    public void NGramTokeniser_ProducesGrams()
    {
        var tokeniser = new NGramTokeniser(minGram: 2, maxGram: 3);
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("abcd", sink);
        Assert.True(sink.Count >= 3, $"NGramTokeniser expected >=3 grams, got {sink.Count}");
    }

    [Fact]
    public void WhitespaceTokeniser_SplitsOnWhitespace()
    {
        var tokeniser = new WhitespaceTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise(" a  b c ", sink);
        Assert.True(sink.Count == 3, $"WhitespaceTokeniser expected 3 tokens, got {sink.Count}");
    }

    [Fact]
    public void LetterTokeniser_SplitsOnNonLetters()
    {
        var tokeniser = new LetterTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("hello, world!", sink);
        Assert.True(sink.Count == 2, $"LetterTokeniser expected 2 tokens, got {sink.Count}");
    }

    [Fact]
    public void IcuTokeniser_Parameterless()
    {
        var tokeniser = new IcuTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("Hello, world!", sink);
        Assert.True(sink.Count >= 2, $"IcuTokeniser expected >=2 tokens, got {sink.Count}");
    }

    [Fact]
    public void IcuTokeniser_EmptyInput()
    {
        var tokeniser = new IcuTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("", sink);
        Assert.True(sink.Count == 0, $"IcuTokeniser empty input expected 0 tokens, got {sink.Count}");
    }

    [Fact]
    public void IcuTokeniser_MixedScripts()
    {
        var tokeniser = new IcuTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("hello привет", sink);
        Assert.True(sink.Count >= 2, $"IcuTokeniser mixed script expected >=2 tokens, got {sink.Count}");
    }

    [Fact]
    public void IcuTokeniser_Numbers()
    {
        var tokeniser = new IcuTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("123 456", sink);
        Assert.True(sink.Tokens.Count == 2,
            $"IcuTokeniser numbers expected 2 tokens, got {sink.Tokens.Count}");
        Assert.True(sink.Tokens.All(t => t.Type == "number"),
            "IcuTokeniser expected all tokens to have type 'number'");
    }

    [Fact]
    public void IcuTokeniser_WithThaiTokeniser()
    {
        var thaiLexicon = new[] { "กา", "กาแฟ", "สวัสดี", "hello" };
        var thaiTokeniser = new ThaiTokeniser(thaiLexicon);
        var tokeniser = new IcuTokeniser(thaiTokeniser);
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("สวัสดี hello", sink);
        Assert.True(sink.Count >= 2, $"IcuTokeniser + ThaiTokeniser expected >=2 tokens, got {sink.Count}");
    }

    [Fact]
    public void KeywordTokeniser_NormalInput()
    {
        var tokeniser = new KeywordTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("hello world", sink);
        Assert.True(sink.Count == 1, $"KeywordTokeniser expected 1 token, got {sink.Count}");
    }

    [Fact]
    public void KeywordTokeniser_EmptyInput()
    {
        var tokeniser = new KeywordTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("", sink);
        Assert.True(sink.Count == 0, $"KeywordTokeniser empty input expected 0 tokens, got {sink.Count}");
    }

    [Fact]
    public void KeywordTokeniser_WhitespaceOnly()
    {
        var tokeniser = new KeywordTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("   ", sink);
        Assert.True(sink.Count == 1, $"KeywordTokeniser whitespace-only expected 1 token, got {sink.Count}");
    }

    [Fact]
    public void KeywordTokeniser_LongInput()
    {
        var tokeniser = new KeywordTokeniser();
        var sink = new CountingTokenSink();
        var longInput = new string('x', 200);
        tokeniser.Tokenise(longInput, sink);
        Assert.True(sink.Count == 1, $"KeywordTokeniser long input expected 1 token, got {sink.Count}");
    }

    [Fact]
    public void ThaiTokeniser_LongestMatch()
    {
        var thaiLexicon = new[] { "กา", "กาแฟ" };
        var tokeniser = new ThaiTokeniser(thaiLexicon);
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("กาแฟ", sink);
        Assert.True(sink.Tokens.Count == 1,
            $"ThaiTokeniser longest-match expected 1 token, got {sink.Tokens.Count}");
        Assert.True(sink.Tokens[0].Type == ThaiTokeniser.ThaiType,
            $"ThaiTokeniser expected type '{ThaiTokeniser.ThaiType}', got '{sink.Tokens[0].Type}'");
    }

    [Fact]
    public void ThaiTokeniser_UnknownGraphemes()
    {
        var thaiLexicon = new[] { "กา" };
        var tokeniser = new ThaiTokeniser(thaiLexicon);
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("\u0E01\u0E02", sink);
        Assert.True(sink.Count >= 1, $"ThaiTokeniser grapheme fallback expected >=1 token, got {sink.Count}");
    }

    [Fact]
    public void ThaiTokeniser_EmptyInput()
    {
        var thaiLexicon = new[] { "กา" };
        var tokeniser = new ThaiTokeniser(thaiLexicon);
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("", sink);
        Assert.True(sink.Count == 0, $"ThaiTokeniser empty input expected 0 tokens, got {sink.Count}");
    }

    [Fact]
    public void ThaiTokeniser_MixedThaiAndLatin()
    {
        var thaiLexicon = new[] { "กาแฟ", "hello", "world" };
        var tokeniser = new ThaiTokeniser(thaiLexicon);
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("hello กาแฟ world", sink);
        Assert.True(sink.Count >= 3, $"ThaiTokeniser mixed Thai+Latin expected >=3 tokens, got {sink.Count}");
    }

    [Fact]
    public void Uax29UrlEmailTokeniser_Url()
    {
        var tokeniser = new Uax29UrlEmailTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("https://example.com/path", sink);
        Assert.True(sink.Tokens.Count == 1,
            $"Uax29UrlEmailTokeniser URL expected 1 token, got {sink.Tokens.Count}");
        Assert.True(sink.Tokens[0].Type == Uax29UrlEmailTokeniser.UrlType,
            $"Uax29UrlEmailTokeniser URL expected type '{Uax29UrlEmailTokeniser.UrlType}', got '{sink.Tokens[0].Type}'");
    }

    [Fact]
    public void Uax29UrlEmailTokeniser_Email()
    {
        var tokeniser = new Uax29UrlEmailTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("user@example.com", sink);
        Assert.True(sink.Tokens.Count == 1,
            $"Uax29UrlEmailTokeniser email expected 1 token, got {sink.Tokens.Count}");
        Assert.True(sink.Tokens[0].Type == Uax29UrlEmailTokeniser.EmailType,
            $"Uax29UrlEmailTokeniser email expected type '{Uax29UrlEmailTokeniser.EmailType}', got '{sink.Tokens[0].Type}'");
    }

    [Fact]
    public void Uax29UrlEmailTokeniser_Hashtag()
    {
        var tokeniser = new Uax29UrlEmailTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("#hello", sink);
        Assert.True(sink.Tokens.Count == 1,
            $"Uax29UrlEmailTokeniser hashtag expected 1 token, got {sink.Tokens.Count}");
        Assert.True(sink.Tokens[0].Type == Uax29UrlEmailTokeniser.HashtagType,
            $"Uax29UrlEmailTokeniser hashtag expected type '{Uax29UrlEmailTokeniser.HashtagType}', got '{sink.Tokens[0].Type}'");
    }

    [Fact]
    public void Uax29UrlEmailTokeniser_Mention()
    {
        var tokeniser = new Uax29UrlEmailTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("@user", sink);
        Assert.True(sink.Tokens.Count == 1,
            $"Uax29UrlEmailTokeniser mention expected 1 token, got {sink.Tokens.Count}");
        Assert.True(sink.Tokens[0].Type == Uax29UrlEmailTokeniser.MentionType,
            $"Uax29UrlEmailTokeniser mention expected type '{Uax29UrlEmailTokeniser.MentionType}', got '{sink.Tokens[0].Type}'");
    }

    [Fact]
    public void Uax29UrlEmailTokeniser_MixedContent()
    {
        var tokeniser = new Uax29UrlEmailTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("visit https://a.com or email x@y.com #tag", sink);
        Assert.True(sink.Tokens.Count >= 3,
            $"Uax29UrlEmailTokeniser mixed expected >=3 tokens, got {sink.Tokens.Count}");
        var types = sink.Tokens.Select(t => t.Type).ToHashSet();
        Assert.True(types.Contains(Uax29UrlEmailTokeniser.UrlType),
            "Uax29UrlEmailTokeniser mixed missing URL type");
        Assert.True(types.Contains(Uax29UrlEmailTokeniser.EmailType),
            "Uax29UrlEmailTokeniser mixed missing email type");
        Assert.True(types.Contains(Uax29UrlEmailTokeniser.HashtagType),
            "Uax29UrlEmailTokeniser mixed missing hashtag type");
    }

    [Fact]
    public void Uax29UrlEmailTokeniser_EmptyInput()
    {
        var tokeniser = new Uax29UrlEmailTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("", sink);
        Assert.True(sink.Count == 0,
            $"Uax29UrlEmailTokeniser empty input expected 0 tokens, got {sink.Count}");
    }

    [Fact]
    public void Uax29UrlEmailTokeniser_WithThaiTokeniser()
    {
        var thaiLexicon = new[] { "กาแฟ" };
        var thaiTokeniser = new ThaiTokeniser(thaiLexicon);
        var tokeniser = new Uax29UrlEmailTokeniser(thaiTokeniser);
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("https://a.com กาแฟ", sink);
        Assert.True(sink.Count >= 2,
            $"Uax29UrlEmailTokeniser + ThaiTokeniser expected >=2 tokens, got {sink.Count}");
    }

    [Fact]
    public void MediaWikiTokeniser_Heading()
    {
        var tokeniser = new MediaWikiTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("== Hello ==", sink);
        Assert.True(sink.Tokens.Count >= 1,
            $"MediaWikiTokeniser heading expected >=1 token, got {sink.Tokens.Count}");
        Assert.True(sink.Tokens.Any(t => t.Type == MediaWikiTokeniser.HeadingType),
            $"MediaWikiTokeniser heading missing type '{MediaWikiTokeniser.HeadingType}'");
    }

    [Fact]
    public void MediaWikiTokeniser_Bold()
    {
        var tokeniser = new MediaWikiTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("'''bold text'''", sink);
        Assert.True(sink.Tokens.Count >= 1,
            $"MediaWikiTokeniser bold expected >=1 token, got {sink.Tokens.Count}");
        Assert.True(sink.Tokens.Any(t => t.Type == MediaWikiTokeniser.BoldType),
            $"MediaWikiTokeniser bold missing type '{MediaWikiTokeniser.BoldType}'");
    }

    [Fact]
    public void MediaWikiTokeniser_Italic()
    {
        var tokeniser = new MediaWikiTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("''italic text''", sink);
        Assert.True(sink.Tokens.Count >= 1,
            $"MediaWikiTokeniser italic expected >=1 token, got {sink.Tokens.Count}");
        Assert.True(sink.Tokens.Any(t => t.Type == MediaWikiTokeniser.ItalicType),
            $"MediaWikiTokeniser italic missing type '{MediaWikiTokeniser.ItalicType}'");
    }

    [Fact]
    public void MediaWikiTokeniser_InternalLink()
    {
        var tokeniser = new MediaWikiTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("[[Page]]", sink);
        Assert.True(sink.Tokens.Count >= 1,
            $"MediaWikiTokeniser internal link expected >=1 token, got {sink.Tokens.Count}");
        Assert.True(sink.Tokens.Any(t => t.Type == MediaWikiTokeniser.InternalLinkType),
            $"MediaWikiTokeniser internal link missing type '{MediaWikiTokeniser.InternalLinkType}'");
    }

    [Fact]
    public void MediaWikiTokeniser_PipedLink()
    {
        var tokeniser = new MediaWikiTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("[[Page|Label Here]]", sink);
        Assert.True(sink.Tokens.Count >= 1,
            $"MediaWikiTokeniser piped link expected >=1 token, got {sink.Tokens.Count}");
        Assert.True(sink.Tokens.Any(t => t.Type == MediaWikiTokeniser.InternalLinkType),
            $"MediaWikiTokeniser piped link missing type '{MediaWikiTokeniser.InternalLinkType}'");
    }

    [Fact]
    public void MediaWikiTokeniser_ExternalLink()
    {
        var tokeniser = new MediaWikiTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("[https://example.com label]", sink);
        Assert.True(sink.Tokens.Count >= 2,
            $"MediaWikiTokeniser external link expected >=2 tokens, got {sink.Tokens.Count}");
        Assert.True(sink.Tokens.Any(t => t.Type == MediaWikiTokeniser.ExternalLinkType),
            $"MediaWikiTokeniser external link missing type '{MediaWikiTokeniser.ExternalLinkType}'");
    }

    [Fact]
    public void MediaWikiTokeniser_Category()
    {
        var tokeniser = new MediaWikiTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("[[Category:Science]]", sink);
        Assert.True(sink.Tokens.Count >= 1,
            $"MediaWikiTokeniser category expected >=1 token, got {sink.Tokens.Count}");
        Assert.True(sink.Tokens.Any(t => t.Type == MediaWikiTokeniser.CategoryType),
            $"MediaWikiTokeniser category missing type '{MediaWikiTokeniser.CategoryType}'");
    }

    [Fact]
    public void MediaWikiTokeniser_Citation()
    {
        var tokeniser = new MediaWikiTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("<ref>Author, 2025</ref>", sink);
        Assert.True(sink.Tokens.Count >= 1,
            $"MediaWikiTokeniser citation expected >=1 token, got {sink.Tokens.Count}");
        Assert.True(sink.Tokens.Any(t => t.Type == MediaWikiTokeniser.CitationType),
            $"MediaWikiTokeniser citation missing type '{MediaWikiTokeniser.CitationType}'");
    }

    [Fact]
    public void MediaWikiTokeniser_MixedMarkup()
    {
        var tokeniser = new MediaWikiTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("Some '''bold''' and [[Link]] text.", sink);
        Assert.True(sink.Tokens.Count >= 3,
            $"MediaWikiTokeniser mixed markup expected >=3 tokens, got {sink.Tokens.Count}");
        var types = sink.Tokens.Select(t => t.Type).ToHashSet();
        Assert.True(types.Contains(MediaWikiTokeniser.BoldType),
            "MediaWikiTokeniser mixed missing bold type");
        Assert.True(types.Contains(MediaWikiTokeniser.InternalLinkType),
            "MediaWikiTokeniser mixed missing internal-link type");
        Assert.True(types.Contains(Token.DefaultType),
            "MediaWikiTokeniser mixed missing default body-text type");
    }

    [Fact]
    public void MediaWikiTokeniser_EmptyInput()
    {
        var tokeniser = new MediaWikiTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("", sink);
        Assert.True(sink.Count == 0,
            $"MediaWikiTokeniser empty input expected 0 tokens, got {sink.Count}");
    }

    // =========================================================================
    // Filter smoke
    // =========================================================================

    [Fact]
    public void PatternReplaceFilter_Regex()
    {
        var filter = new PatternReplaceFilter("[0-9]+", "#");
        var localSink = new MaterialisingTokenSink();
        filter.Apply("call12345now".AsSpan(), 0, 12, Token.DefaultType, 1, null, localSink);
        Assert.True(localSink.Tokens.Count == 1 && localSink.Tokens[0].Text == "call#now",
            $"PatternReplaceFilter expected 'call#now', got '{(localSink.Tokens.Count > 0 ? localSink.Tokens[0].Text : "null")}'");
    }

    [Fact]
    public void PatternReplaceFilter_CompiledRegex()
    {
        var regex = new System.Text.RegularExpressions.Regex(@"\s+",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            System.TimeSpan.FromSeconds(1));
        var filter = new PatternReplaceFilter(regex, "-");
        var localSink = new MaterialisingTokenSink();
        filter.Apply("hello world".AsSpan(), 0, 11, Token.DefaultType, 1, null, localSink);
        Assert.True(localSink.Tokens.Count == 1 && localSink.Tokens[0].Text == "hello-world",
            "PatternReplaceFilter(Regex) expected 'hello-world'");
    }

    [Fact]
    public void PatternReplaceCharFilter()
    {
        var filter = new PatternReplaceCharFilter(@"\d+", "#");
        var result = filter.Filter("abc123def456".AsSpan());
        Assert.True(result == "abc#def#",
            $"PatternReplaceCharFilter expected 'abc#def#', got '{result}'");
    }

    [Fact]
    public void SynonymGraphFilter()
    {
        var map = new SynonymMap();
        map.Add("quick brown", ["fast", "brown"]);
        map.Add("lazy", ["idle"]);
        var analyser = new Analyser(
            new Tokeniser(),
            new LowercaseFilter(),
            new SynonymGraphFilter(map));
        var localSink = new CapturingTokenSink();
        analyser.Analyse("the quick brown fox", localSink);
        Assert.True(localSink.Count >= 3, $"SynonymGraphFilter expected >=3 tokens, got {localSink.Count}");
    }

    [Fact]
    public void HunspellStemFilter()
    {
        var aff = "SET UTF-8\nTRY abcdefghijklmnopqrstuvwxyz\n\nPFX A Y 1\nPFX A 0 re .\n\nSFX B Y 1\nSFX B 0 ing .\n";
        var dic = "3\nrun\nwalk\njump\n";
        var dict = HunspellDictionary.Parse(aff, dic, maxGeneratedFormsPerEntry: 64);
        Assert.True(dict is not null, "HunspellDictionary.Parse returned null");

        var filter = new HunspellStemFilter(dict!);
        var sink = new CountingTokenSink();
        filter.Apply("running".AsSpan(), 0, 7, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count >= 1, $"HunspellStemFilter expected >=1 stem, got {sink.Count}");
    }

    [Fact]
    public void ShingleFilter()
    {
        var shingle = new ShingleFilter(minShingleSize: 2, maxShingleSize: 3, outputUnigrams: false);
        var sink = new CountingTokenSink();
        shingle.Apply("the".AsSpan(), 0, 3, Token.DefaultType, 1, null, sink);
        shingle.Apply("quick".AsSpan(), 4, 9, Token.DefaultType, 1, null, sink);
        shingle.Apply("brown".AsSpan(), 10, 15, Token.DefaultType, 1, null, sink);
        ((ISpanTokenFilter)shingle).Finish(sink);
        Assert.True(sink.Count >= 2, $"ShingleFilter expected >=2 shingles, got {sink.Count}");
    }

    [Fact]
    public void WordDelimiterFilter()
    {
        var wdf = new WordDelimiterFilter();
        var sink = new CountingTokenSink();
        wdf.Apply("Wi-Fi".AsSpan(), 0, 5, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count >= 1, $"WordDelimiterFilter expected >=1 token, got {sink.Count}");
    }

    [Fact]
    public void LowercaseFilter()
    {
        var lc = new LowercaseFilter();
        var sink = new CountingTokenSink();
        lc.Apply("HELLO".AsSpan(), 0, 5, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count >= 1, $"LowercaseFilter expected >=1 token, got {sink.Count}");
    }

    [Fact]
    public void ClassicFilter()
    {
        var cf = new ClassicFilter();
        var sink = new CountingTokenSink();
        cf.Apply("Wi-Fi.".AsSpan(), 0, 6, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count >= 1, $"ClassicFilter expected >=1 token, got {sink.Count}");
    }

    [Fact]
    public void StopWordFilter()
    {
        var sw = new StopWordFilter(StopWords.English);
        var sink = new CountingTokenSink();
        sw.Apply("the".AsSpan(), 0, 3, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count == 0, $"StopWordFilter expected 0 tokens for 'the', got {sink.Count}");
    }

    [Fact]
    public void CommonGramsFilter()
    {
        var words = System.Collections.Frozen.FrozenSet.ToFrozenSet(["of", "in"]);
        var cg = new CommonGramsFilter(words);
        var sink = new CountingTokenSink();
        cg.Apply("king".AsSpan(), 0, 4, Token.DefaultType, 1, null, sink);
        cg.Apply("of".AsSpan(), 5, 7, Token.DefaultType, 1, null, sink);
        cg.Finish(sink);
        Assert.True(sink.Count >= 1, $"CommonGramsFilter expected >=1 token, got {sink.Count}");
    }

    [Fact]
    public void KeepWordFilter()
    {
        var words = System.Collections.Frozen.FrozenSet.ToFrozenSet(["keep"]);
        var kw = new KeepWordFilter(words);
        var sink = new CountingTokenSink();
        kw.Apply("keep".AsSpan(), 0, 4, Token.DefaultType, 1, null, sink);
        kw.Apply("drop".AsSpan(), 0, 4, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count == 1, $"KeepWordFilter expected 1 token, got {sink.Count}");
    }

    [Fact]
    public void LengthFilter()
    {
        var lf = new LengthFilter(minLength: 3, maxLength: 5);
        var sink = new CountingTokenSink();
        lf.Apply("hi".AsSpan(), 0, 2, Token.DefaultType, 1, null, sink);
        lf.Apply("hello".AsSpan(), 0, 5, Token.DefaultType, 1, null, sink);
        lf.Apply("greetings".AsSpan(), 0, 9, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count == 1, $"LengthFilter expected 1 token, got {sink.Count}");
    }

    [Fact]
    public void LimitTokenCountFilter()
    {
        var lt = new LimitTokenCountFilter(maxTokenCount: 2);
        var sink = new CountingTokenSink();
        lt.Apply("a".AsSpan(), 0, 1, Token.DefaultType, 1, null, sink);
        lt.Apply("b".AsSpan(), 0, 1, Token.DefaultType, 1, null, sink);
        lt.Apply("c".AsSpan(), 0, 1, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count == 2, $"LimitTokenCountFilter expected 2 tokens, got {sink.Count}");
    }

    [Fact]
    public void ReverseStringFilter()
    {
        var rs = new ReverseStringFilter();
        var sink = new CountingTokenSink();
        rs.Apply("abc".AsSpan(), 0, 3, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count >= 1, $"ReverseStringFilter expected >=1 token, got {sink.Count}");
    }

    [Fact]
    public void ElisionFilter()
    {
        var elision = new ElisionFilter(["l'", "d'"]);
        var sink = new CountingTokenSink();
        elision.Apply("l'avion".AsSpan(), 0, 7, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count == 1, $"ElisionFilter expected 1 token, got {sink.Count}");
    }

    [Fact]
    public void KeywordMarkerFilter()
    {
        var km = new KeywordMarkerFilter(["important"]);
        var sink = new CountingTokenSink();
        km.Apply("important".AsSpan(), 0, 9, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count == 1, $"KeywordMarkerFilter expected 1 token, got {sink.Count}");
    }

    [Fact]
    public void TruncateTokenFilter()
    {
        var tt = new TruncateTokenFilter(3);
        var sink = new CountingTokenSink();
        tt.Apply("abcdef".AsSpan(), 0, 6, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count == 1, $"TruncateTokenFilter expected 1 token, got {sink.Count}");
    }

    [Fact]
    public void TypeTokenFilter()
    {
        var tt = new TypeTokenFilter(["keepme"]);
        var sink = new CountingTokenSink();
        tt.Apply("text".AsSpan(), 0, 4, "keepme", 1, null, sink);
        tt.Apply("text".AsSpan(), 0, 4, "dropme", 1, null, sink);
        Assert.True(sink.Count == 1, $"TypeTokenFilter expected 1 token, got {sink.Count}");
    }

    [Fact]
    public void FlattenGraphFilter()
    {
        var fg = new FlattenGraphFilter();
        var sink = new CountingTokenSink();
        fg.Apply("a".AsSpan(), 0, 1, Token.DefaultType, 1, null, sink);
        fg.Apply("b".AsSpan(), 0, 1, Token.DefaultType, 0, null, sink);
        Assert.True(sink.Count >= 1, $"FlattenGraphFilter expected >=1 token, got {sink.Count}");
    }

    [Fact]
    public void HyphenatedWordsFilter_Constructor()
    {
        var hw = new HyphenatedWordsFilter();
        Assert.True(hw is not null, "HyphenatedWordsFilter constructor failed");
    }

    [Fact]
    public void DecimalDigitFilter()
    {
        var dd = new DecimalDigitFilter();
        var sink = new MaterialisingTokenSink();
        dd.Apply("\u0661\u0662\u0663".AsSpan(), 0, 3, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Tokens.Count == 1 && sink.Tokens[0].Text == "123",
            $"DecimalDigitFilter expected '123', got '{(sink.Tokens.Count > 0 ? sink.Tokens[0].Text : "null")}'");
    }

    [Fact]
    public void AccentFoldingFilter()
    {
        var af = new AccentFoldingFilter();
        var sink = new CountingTokenSink();
        af.Apply("caf\u00E9".AsSpan(), 0, 4, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count >= 1, $"AccentFoldingFilter expected >=1 token, got {sink.Count}");
    }

    [Fact]
    public void StemTokenFilter_WithEnglishStemmer()
    {
        var stemmer = new EnglishStemmer();
        var stf = new StemTokenFilter(stemmer);
        var sink = new CountingTokenSink();
        stf.Apply("running".AsSpan(), 0, 7, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count >= 1, $"StemTokenFilter(EnglishStemmer) expected >=1 token, got {sink.Count}");
    }

    [Fact]
    public void MetaphoneFilter()
    {
        var mf = new MetaphoneFilter();
        var sink = new CountingTokenSink();
        mf.Apply("smith".AsSpan(), 0, 5, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count >= 1, $"MetaphoneFilter expected >=1 token, got {sink.Count}");
    }

    [Fact]
    public void PhoneticAlternatesFilter()
    {
        var pa = new PhoneticAlternatesFilter(maxExpansions: 3);
        var sink = new CountingTokenSink();
        pa.Apply("meier".AsSpan(), 0, 5, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count >= 1, $"PhoneticAlternatesFilter expected >=1 token, got {sink.Count}");
    }

    [Fact]
    public void CachingTokenFilter()
    {
        var caching = new CachingTokenFilter();
        var analyser = new Analyser(new Tokeniser(), new LowercaseFilter(), caching);
        var localSink = new CapturingTokenSink();
        analyser.Analyse("HELLO WORLD", localSink);
        Assert.True(caching.Tokens.Count >= 2, $"CachingTokenFilter expected >=2 tokens, got {caching.Tokens.Count}");
    }

    // =========================================================================
    // Stemmer smoke
    // =========================================================================

    [Fact]
    public void KStemLexicon_FromWords()
    {
        var lexicon = KStemLexicon.From(["run", "walk", "jump", "swim"]);
        Assert.True(lexicon.Contains("walk"), "KStemLexicon should contain 'walk'");
        Assert.True(!lexicon.Contains("flying"), "KStemLexicon should not contain 'flying'");
        Assert.True(lexicon.ContainsPreLowered("run"), "KStemLexicon.ContainsPreLowered should find 'run'");
        Assert.True(lexicon.Contains("JUMP".AsSpan()), "KStemLexicon span lookup should find 'JUMP'");
    }

    [Fact]
    public void KStemmer_ViaStemTokenFilter()
    {
        var lexicon = KStemLexicon.From(["running", "walk", "walked", "jumped", "jump"]);
        var kstemmer = new KStemmer(lexicon);
        var filter = new StemTokenFilter(kstemmer);
        var sink = new CountingTokenSink();
        filter.Apply("running".AsSpan(), 0, 7, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count >= 1, $"KStemmer expected >=1 token, got {sink.Count}");
    }

    [Fact]
    public void PorterStemmerFilter()
    {
        var porter = new PorterStemmerFilter();
        var sink = new CountingTokenSink();
        porter.Apply("running".AsSpan(), 0, 7, Token.DefaultType, 1, null, sink);
        Assert.True(sink.Count >= 1, $"PorterStemmerFilter expected >=1 token, got {sink.Count}");
    }

    [Fact]
    public void ISpanStemmer_AllImplementations()
    {
        Span<char> buffer = stackalloc char[32];
        foreach (var stemmer in new ISpanStemmer[]
        {
            new EnglishStemmer(),
            new FrenchStemmer(),
            new GermanStemmer(),
            new SpanishStemmer(),
            new ItalianStemmer(),
            new PortugueseStemmer(),
            new DutchStemmer(),
            new RussianStemmer(),
            new ArabicStemmer(),
        })
        {
            "test".AsSpan().CopyTo(buffer[..4]);
            int result = stemmer.Stem(buffer[..4], buffer);
            Assert.True(result >= 1 || result == -1,
                $"{stemmer.GetType().Name}.Stem expected >=1 or -1, got {result}");
        }
    }

    // =========================================================================
    // Analyser factory smoke
    // =========================================================================

    [Fact]
    public void AnalyserFactory_CreatesAllSupportedLanguages()
    {
        foreach (var lang in AnalyserFactory.SupportedLanguages)
        {
            try
            {
                var analyser = AnalyserFactory.Create(lang);
                Assert.True(analyser is not null, $"AnalyserFactory.Create('{lang}') returned null");
            }
            catch (NotSupportedException)
            {
                Assert.Fail($"AnalyserFactory.Create('{lang}') threw NotSupportedException unexpectedly");
            }
        }
    }

    // =========================================================================
    // Custom analyser smoke
    // =========================================================================

    [Fact]
    public void StandardAnalyser()
    {
        var analyser = new StandardAnalyser();
        var sink = new CapturingTokenSink();
        analyser.Analyse("The quick brown fox jumps over the lazy dog.", sink);
        Assert.True(sink.Count >= 5, $"StandardAnalyser expected >=5 tokens, got {sink.Count}");
    }

    [Fact]
    public void SimpleAnalyser()
    {
        var analyser = new SimpleAnalyser();
        var sink = new CapturingTokenSink();
        analyser.Analyse("Hello World", sink);
        Assert.True(sink.Count >= 2, $"SimpleAnalyser expected >=2 tokens, got {sink.Count}");
    }

    [Fact]
    public void WhitespaceAnalyser()
    {
        var analyser = new WhitespaceAnalyser();
        var sink = new CapturingTokenSink();
        analyser.Analyse("hello  world", sink);
        Assert.True(sink.Count == 2, $"WhitespaceAnalyser expected 2 tokens, got {sink.Count}");
    }

    [Fact]
    public void KeywordAnalyser()
    {
        var analyser = new KeywordAnalyser();
        var sink = new CapturingTokenSink();
        analyser.Analyse("entire field as one token", sink);
        Assert.True(sink.Count == 1, $"KeywordAnalyser expected 1 token, got {sink.Count}");
    }

    [Fact]
    public void StemmedAnalyser()
    {
        var analyser = new StemmedAnalyser();
        var sink = new CapturingTokenSink();
        analyser.Analyse("running jumped walking", sink);
        Assert.True(sink.Count >= 3, $"StemmedAnalyser expected >=3 tokens, got {sink.Count}");
    }

    [Fact]
    public void StemmerAnalyser()
    {
        var analyser = new StemmerAnalyser(new EnglishStemmer());
        var sink = new CapturingTokenSink();
        analyser.Analyse("cats dogs running", sink);
        Assert.True(sink.Count >= 3, $"StemmerAnalyser expected >=3 tokens, got {sink.Count}");
    }

    [Fact]
    public void LanguageAnalyser_English()
    {
        var analyser = new LanguageAnalyser(
            new Tokeniser(),
            StopWords.English,
            new EnglishStemmer());
        var sink = new CapturingTokenSink();
        analyser.Analyse("The cats were running quickly", sink);
        Assert.True(sink.Count >= 3, $"LanguageAnalyser(en) expected >=3 tokens, got {sink.Count}");
    }

    [Fact]
    public void LanguageAnalyser_Cjk()
    {
        var analyser = new LanguageAnalyser(
            new CJKBigramTokeniser(),
            StopWords.Chinese,
            stemmer: null);
        var sink = new CapturingTokenSink();
        analyser.Analyse("\u4F60\u597D\u4E16\u754C", sink);
        Assert.True(sink.Count >= 2, $"LanguageAnalyser(zh) expected >=2 tokens, got {sink.Count}");
    }

    [Fact]
    public void IcuAnalyser()
    {
        var analyser = new IcuAnalyser();
        var sink = new CapturingTokenSink();
        analyser.Analyse("Hello world — testing ICU tokenisation.", sink);
        Assert.True(sink.Count >= 3, $"IcuAnalyser expected >=3 tokens, got {sink.Count}");
    }

    // =========================================================================
    // JsonDocumentMapper smoke
    // =========================================================================

    [Fact]
    public void JsonMapper_FromJsonString_Flat()
    {
        var doc = JsonDocumentMapper.FromJsonString(
            """{"id":"abc","title":"Hello","price":9.99}""");
        Assert.True(doc.GetField("id") is not null, "JsonDocumentMapper: id field missing");
        Assert.True(doc.GetField("title") is not null, "JsonDocumentMapper: title field missing");
        Assert.True(doc.GetField("price") is not null, "JsonDocumentMapper: price field missing");
    }

    [Fact]
    public void JsonMapper_FromJsonString_Nested()
    {
        var doc = JsonDocumentMapper.FromJsonString(
            """{"a":{"b":"nested"}}""");
        Assert.True(doc.GetField("a.b") is not null, "JsonDocumentMapper: nested a.b field missing");
    }

    [Fact]
    public void JsonMapper_FromJsonString_Array()
    {
        var doc = JsonDocumentMapper.FromJsonString(
            """{"tags":["alpha","beta"]}""");
        var field = doc.GetField("tags");
        Assert.True(field is not null, "JsonDocumentMapper: tags field missing");
    }

    [Fact]
    public void JsonMapper_FromJsonString_Empty()
    {
        var doc = JsonDocumentMapper.FromJsonString("{}");
        Assert.True(doc is not null, "JsonDocumentMapper: empty object produced null document");
    }

    [Fact]
    public void JsonMapper_FromJson_CustomOptions()
    {
        using var jsonDoc = System.Text.Json.JsonDocument.Parse("""{"x":{"y":1}}""");
        var options = new JsonMappingOptions
        {
            FieldNameSeparator = "_",
            MaxDepth = 5,
        };
        var doc = JsonDocumentMapper.FromJson(jsonDoc.RootElement, options);
        Assert.True(doc.GetField("x_y") is not null,
            "JsonDocumentMapper: custom separator field 'x_y' missing");
    }

    // =========================================================================
    // Telemetry smoke
    // =========================================================================

    [Fact]
    public void NullMetricsCollector_AllOpsAreNoOps()
    {
        var m = NullMetricsCollector.Instance;
        m.RecordSearchLatency(System.TimeSpan.FromMilliseconds(12));
        m.RecordCacheHit();
        m.RecordCacheMiss();
        m.RecordFlush(System.TimeSpan.FromMilliseconds(1));
        m.RecordMerge(System.TimeSpan.FromMilliseconds(2), 1);
        m.RecordCommit(System.TimeSpan.FromMilliseconds(3));
        var snap = m.GetSnapshot();
        Assert.True(snap.SearchCount == 0, "NullMetricsCollector: SearchCount should be 0");
    }

    [Fact]
    public void DefaultMetricsCollector_TracksCounts()
    {
        var m = new DefaultMetricsCollector();
        m.RecordSearchLatency(System.TimeSpan.FromMilliseconds(5));
        m.RecordCacheHit();
        m.RecordCacheHit();
        m.RecordCacheMiss();
        var snap = m.GetSnapshot();
        Assert.True(snap.SearchCount == 1,
            $"DefaultMetricsCollector: SearchCount expected 1, got {snap.SearchCount}");
        Assert.True(snap.CacheHits == 2,
            $"DefaultMetricsCollector: CacheHits expected 2, got {snap.CacheHits}");
        Assert.True(snap.CacheMisses == 1,
            $"DefaultMetricsCollector: CacheMisses expected 1, got {snap.CacheMisses}");
    }

    [Fact]
    public void MeterMetricsCollector_CreatesInstruments()
    {
        var m = new MeterMetricsCollector();
        m.RecordSearchLatency(System.TimeSpan.FromMilliseconds(7));
        m.RecordSearchLatency(System.TimeSpan.FromMilliseconds(3));
        m.RecordCacheHit();
        m.RecordCacheMiss();
        m.RecordFlush(System.TimeSpan.FromMilliseconds(1));
        m.RecordMerge(System.TimeSpan.FromMilliseconds(2), 2);
        m.RecordCommit(System.TimeSpan.FromMilliseconds(3));
        var snap = m.GetSnapshot();
        Assert.True(snap.SearchCount == 2,
            $"MeterMetricsCollector: SearchCount expected 2, got {snap.SearchCount}");
        Assert.True(snap.CacheHits == 1,
            $"MeterMetricsCollector: CacheHits expected 1, got {snap.CacheHits}");
        m.Dispose();
    }

    // =========================================================================
    // Source generator smoke
    // =========================================================================

    [Fact]
    public void SourceGen_CreateSchema()
    {
        var schema = SmokeModelIndex.CreateSchema(strict: true);
        Assert.True(schema is not null, "SourceGen: CreateSchema returned null");
        Assert.True(schema!.StrictMode, "SourceGen: CreateSchema strict should be true");
    }

    [Fact]
    public void SourceGen_ToDocument()
    {
        var model = new SmokeModel { Id = "sg-1", Title = "Source Gen Test", Count = 42 };
        var doc = SmokeModelIndex.ToDocument(model);
        Assert.True(doc is not null, "SourceGen: ToDocument returned null");
        var idField = doc!.GetField("id");
        Assert.True(idField is not null, "SourceGen: id field missing from ToDocument");
    }

    [Fact]
    public void SourceGen_FromStoredDocument()
    {
        var storedFields = new Dictionary<string, IReadOnlyList<string>>
        {
            ["id"] = new[] { "sg-2" },
            ["title"] = new[] { "Stored Test" },
            ["count"] = new[] { "99" },
        };
        var stored = StoredDocument.Create(storedFields, null);
        var revived = SmokeModelIndex.FromStoredDocument(stored);
        Assert.True(revived.Id == "sg-2",
            $"SourceGen: FromStoredDocument Id expected 'sg-2', got '{revived.Id}'");
        Assert.True(revived.Title == "Stored Test",
            "SourceGen: FromStoredDocument Title mismatch");
        Assert.True(revived.Count == 99,
            $"SourceGen: FromStoredDocument Count expected 99, got {revived.Count}");
    }

    [Fact]
    public void SourceGen_MapSingleton()
    {
        var map = SmokeModelIndex.Map;
        Assert.True(map is not null, "SourceGen: Map singleton is null");
        Assert.True(map!.DocumentName == "SmokeModel",
            $"SourceGen: DocumentName expected 'SmokeModel', got '{map.DocumentName}'");
    }

    [Fact]
    public void SourceGen_FieldsStaticClass()
    {
        Assert.True(SmokeModelIndex.Fields.Id.Name == "id",
            "SourceGen: Fields.Id.Name mismatch");
        Assert.True(SmokeModelIndex.Fields.Title.Name == "title",
            "SourceGen: Fields.Title.Name mismatch");
        Assert.True(SmokeModelIndex.Fields.Count.Name == "count",
            "SourceGen: Fields.Count.Name mismatch");
    }
}

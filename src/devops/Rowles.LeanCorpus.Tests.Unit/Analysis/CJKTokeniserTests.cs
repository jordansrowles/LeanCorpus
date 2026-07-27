using System.Text;

using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;

using Xunit;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

/// <summary>
/// Behavioural tests for Chinese, Japanese, Korean and ideograph tokenisation.
/// </summary>
public sealed class CJKTokeniserTests
{
    #region CJKBigramTokeniser fixes

    [Fact(DisplayName = "CJKBigramTokeniser: Hangul not bigrammed")]
    public void CJKBigramTokeniser_Hangul_NotBigrammed()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("\uD55C\uAD6D\uC5B4", sink); // 한국어
        Assert.Single(sink.Tokens);
        Assert.Equal("\uD55C\uAD6D\uC5B4", sink.Tokens[0].Text);
    }

    [Fact(DisplayName = "CJKBigramTokeniser: Katakana not bigrammed")]
    public void CJKBigramTokeniser_Katakana_NotBigrammed()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("\u30C6\u30B9\u30C8", sink); // テスト
        Assert.Single(sink.Tokens);
        Assert.Equal("\u30C6\u30B9\u30C8", sink.Tokens[0].Text);
    }

    [Fact(DisplayName = "CJKBigramTokeniser: Hiragana not bigrammed")]
    public void CJKBigramTokeniser_Hiragana_NotBigrammed()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("\u3072\u3089\u304C\u306A", sink); // ひらがな
        Assert.Single(sink.Tokens);
        Assert.Equal("\u3072\u3089\u304C\u306A", sink.Tokens[0].Text);
    }

    [Fact(DisplayName = "CJKBigramTokeniser: Mixed kanji + hiragana split correctly")]
    public void CJKBigramTokeniser_MixedScript_KanjiBigrammed_KanaWord()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("\u98DF\u3079\u308B", sink); // 食べる (kanji 食 + hiragana べる)
        Assert.Equal(2, sink.Tokens.Count);
        Assert.Equal("\u98DF", sink.Tokens[0].Text);  // 食 (single CJK unigram)
        Assert.Equal("\u3079\u308B", sink.Tokens[1].Text); // べる (kana word)
    }

    [Fact(DisplayName = "CJKBigramTokeniser: CJK compatibility ideographs bigrammed")]
    public void CJKBigramTokeniser_CjkCompatibilityIdeographs_Bigrammed()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("\uFA0E\uFA0F", sink); // U+FA0E, U+FA0F
        Assert.Single(sink.Tokens);
        Assert.Equal("\uFA0E\uFA0F", sink.Tokens[0].Text);
    }

    [Fact(DisplayName = "CJKBigramTokeniser: Extension B surrogate pairs not dropped")]
    public void CJKBigramTokeniser_SurrogatePair_CjkExtensionB_NotDropped()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new MaterialisingTokenSink();
        // U+20000 and U+20001 are CJK Extension B (supplementary plane, surrogate pairs in UTF-16)
        var input = "\U00020000\U00020001";
        tokeniser.Tokenise(input, sink);
        Assert.True(sink.Tokens.Count >= 1, $"CJK Extension B characters must not be silently dropped; got {sink.Tokens.Count} tokens");
        // The combined text of all tokens should cover both characters
        var combined = string.Concat(sink.Tokens.Select(t => t.Text));
        Assert.Equal(input, combined);
    }

    [Theory(DisplayName = "CJKBigramTokeniser: All supplementary ideograph blocks are retained")]
    [InlineData("\U0002A700\U0002A701")]
    [InlineData("\U0002B740\U0002B741")]
    [InlineData("\U0002B820\U0002B821")]
    [InlineData("\U0002CEB0\U0002CEB1")]
    [InlineData("\U00030000\U00030001")]
    [InlineData("\U00031350\U00031351")]
    public void CJKBigramTokeniser_SupplementaryIdeographBlocks_NotDropped(string input)
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new MaterialisingTokenSink();

        tokeniser.Tokenise(input, sink);

        Assert.Single(sink.Tokens);
        Assert.Equal(input, sink.Tokens[0].Text);
        Assert.Equal(CJKBigramTokeniser.CjkType, sink.Tokens[0].Type);
    }

    [Fact(DisplayName = "CJKBigramTokeniser: CJK tokens get 'cjk' type classification")]
    public void CJKBigramTokeniser_CjkRun_EmitsTypeClassification()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("\u4E2D\u6587", sink); // 中文
        Assert.NotEmpty(sink.Tokens);
        Assert.All(sink.Tokens, t => Assert.Equal("cjk", t.Type));
    }

    [Fact(DisplayName = "CJKBigramTokeniser: Non-CJK word keeps existing type classification")]
    public void CJKBigramTokeniser_NonCjkWord_KeepsExistingType()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("hello", sink);
        Assert.Single(sink.Tokens);
        Assert.Equal(Token.DefaultType, sink.Tokens[0].Type); // "term"
    }

    [Fact(DisplayName = "CJKBigramTokeniser: Punctuation only produces no tokens")]
    public void CJKBigramTokeniser_PunctuationOnly_NoTokens()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("!@#$%", sink);
        Assert.Empty(sink.Tokens);
    }

    [Fact(DisplayName = "CJKBigramTokeniser: Empty string produces no tokens")]
    public void CJKBigramTokeniser_EmptyString_NoTokens()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("", sink);
        Assert.Empty(sink.Tokens);
    }

    [Fact(DisplayName = "CJKBigramTokeniser: ASCII-only standard tokenisation unaffected")]
    public void CJKBigramTokeniser_AsciiOnly_StandardTokenisation()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("hello world", sink);
        Assert.Equal(2, sink.Tokens.Count);
        Assert.Equal("hello", sink.Tokens[0].Text);
        Assert.Equal("world", sink.Tokens[1].Text);
    }

    [Fact(DisplayName = "CJKBigramTokeniser: Two CJK chars produce one bigram")]
    public void CJKBigramTokeniser_TwoChars_OneBigram()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("\u4E2D\u6587", sink); // 中文
        Assert.Single(sink.Tokens);
        Assert.Equal("\u4E2D\u6587", sink.Tokens[0].Text);
    }

    [Fact(DisplayName = "CJKBigramTokeniser: Three CJK chars produce two overlapping bigrams")]
    public void CJKBigramTokeniser_ThreeChars_TwoBigrams()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("\u4E2D\u6587\u6D4B", sink); // 中文测
        Assert.Equal(2, sink.Tokens.Count);
        Assert.Equal("\u4E2D\u6587", sink.Tokens[0].Text); // 中文
        Assert.Equal("\u6587\u6D4B", sink.Tokens[1].Text); // 文测
    }

    #endregion

    #region Stop word behaviour

    [Fact(DisplayName = "CJKBigramTokeniser: Chinese stop word not filtered by bigram output")]
    public void CJKBigramTokeniser_ChineseStopWord_NotFilteredByBigram()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new MaterialisingTokenSink();
        // 我的猫: 的 is a Chinese stop word but appears in bigrams 我的 and 的猫
        tokeniser.Tokenise("\u6211\u7684\u732B", sink); // 我的猫
        // With CJKBigramTokeniser, the 3 chars produce 2 bigrams: 我的, 的猫
        Assert.Equal(2, sink.Tokens.Count);
        Assert.Contains(sink.Tokens, t => t.Text.Contains('\u7684')); // 的 embedded in bigrams
    }

    [Fact(DisplayName = "ChineseLexiconTokeniser: OOV stop word is emitted before filtering")]
    public void ChineseLexiconTokeniser_OovStopWord_EmittedBeforeFiltering()
    {
        // The tokeniser emits OOV CJK characters as unigrams. Stop word
        // filtering happens at the LanguageAnalyser level, not the tokeniser.
        var lexicon = new List<string> { "\u6211", "\u732B" }; // 我, 猫
        var tokeniser = new ChineseLexiconTokeniser(lexicon);
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("\u6211\u7684\u732B", sink); // 我的猫 (的 excluded from lexicon)

        Assert.Equal(3, sink.Tokens.Count);
        Assert.Equal("\u6211", sink.Tokens[0].Text); // 我 (lexicon match)
        Assert.Equal("\u7684", sink.Tokens[1].Text); // 的 (OOV unigram)
        Assert.Equal("\u732B", sink.Tokens[2].Text); // 猫 (lexicon match)
    }

    #endregion

    #region ChineseLexiconTokeniser

    [Fact(DisplayName = "ChineseLexiconTokeniser: Longest match preferred over subwords")]
    public void ChineseLexiconTokeniser_LongestMatch_Preferred()
    {
        var lexicon = new List<string> { "\u4E2D\u56FD", "\u4E2D", "\u56FD" }; // 中国, 中, 国
        var tokeniser = new ChineseLexiconTokeniser(lexicon);
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("\u4E2D\u56FD", sink); // 中国
        Assert.Single(sink.Tokens);
        Assert.Equal("\u4E2D\u56FD", sink.Tokens[0].Text); // 中国 (max match)
    }

    [Fact(DisplayName = "ChineseLexiconTokeniser: Unknown characters fall back to unigram")]
    public void ChineseLexiconTokeniser_UnknownCharacters_FallbackUnigram()
    {
        var lexicon = new List<string> { "\u4E2D" }; // 中 only
        var tokeniser = new ChineseLexiconTokeniser(lexicon);
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("\u4E2D\u6587", sink); // 中文 (文 is OOV)
        Assert.Equal(2, sink.Tokens.Count);
        Assert.Equal("\u4E2D", sink.Tokens[0].Text); // 中
        Assert.Equal("\u6587", sink.Tokens[1].Text);  // 文 (fallback unigram)
    }

    [Fact(DisplayName = "ChineseLexiconTokeniser: All unknown characters emitted as unigrams")]
    public void ChineseLexiconTokeniser_AllUnknown_Unigrams()
    {
        var lexicon = new List<string> { "\u732B" }; // 猫 only
        var tokeniser = new ChineseLexiconTokeniser(lexicon);
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("\u4F60\u597D", sink); // 你好 (neither in lexicon)
        Assert.Equal(2, sink.Tokens.Count);
        Assert.Equal("\u4F60", sink.Tokens[0].Text); // 你
        Assert.Equal("\u597D", sink.Tokens[1].Text); // 好
    }

    [Fact(DisplayName = "ChineseLexiconTokeniser: Mixed CJK and ASCII preserved")]
    public void ChineseLexiconTokeniser_MixedCjkAndAscii()
    {
        var lexicon = new List<string> { "\u4F60\u597D" }; // 你好
        var tokeniser = new ChineseLexiconTokeniser(lexicon);
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("hello\u4F60\u597Dworld", sink); // hello你好world
        Assert.Equal(3, sink.Tokens.Count);
        Assert.Equal("hello", sink.Tokens[0].Text);
        Assert.Equal("\u4F60\u597D", sink.Tokens[1].Text); // 你好
        Assert.Equal("world", sink.Tokens[2].Text);
    }

    [Fact(DisplayName = "ChineseLexiconTokeniser: Empty string produces no tokens")]
    public void ChineseLexiconTokeniser_EmptyString_NoTokens()
    {
        var tokeniser = new ChineseLexiconTokeniser(new[] { "\u4E2D" });
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("", sink);
        Assert.Empty(sink.Tokens);
    }

    [Fact(DisplayName = "ChineseLexiconTokeniser: Null lexicon throws ArgumentNullException")]
    public void ChineseLexiconTokeniser_NullLexicon_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ChineseLexiconTokeniser(null!));
    }

    [Fact(DisplayName = "ChineseLexiconTokeniser: Empty lexicon throws ArgumentException")]
    public void ChineseLexiconTokeniser_EmptyLexicon_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ChineseLexiconTokeniser(Array.Empty<string>()));
    }

    [Fact(DisplayName = "ChineseLexiconTokeniser: FromStream loads lexicon correctly")]
    public void ChineseLexiconTokeniser_FromStream_LoadsCorrectly()
    {
        var lexiconText = "\u4E2D\u56FD\n\u4E2D\u6587\n"; // 中国, 中文
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(lexiconText));
        var tokeniser = ChineseLexiconTokeniser.FromStream(stream);
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("\u4E2D\u56FD", sink); // 中国
        Assert.Single(sink.Tokens);
        Assert.Equal("\u4E2D\u56FD", sink.Tokens[0].Text);
    }

    [Fact(DisplayName = "ChineseLexiconTokeniser: CJK tokens get 'cjk' type")]
    public void ChineseLexiconTokeniser_TypeClassification_CjkType()
    {
        var lexicon = new List<string> { "\u4E2D" };
        var tokeniser = new ChineseLexiconTokeniser(lexicon);
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("\u4E2D", sink); // 中
        Assert.Single(sink.Tokens);
        Assert.Equal("cjk", sink.Tokens[0].Type);
    }

    [Fact(DisplayName = "ChineseLexiconTokeniser: Supplementary ideographs remain complete")]
    public void ChineseLexiconTokeniser_SupplementaryIdeographs_FallbackByCodePoint()
    {
        var tokeniser = new ChineseLexiconTokeniser(new[] { "\u4E2D" });
        var sink = new MaterialisingTokenSink();
        var input = "\U00020000\U00020001";

        tokeniser.Tokenise(input, sink);

        Assert.Equal(2, sink.Tokens.Count);
        Assert.Equal("\U00020000", sink.Tokens[0].Text);
        Assert.Equal("\U00020001", sink.Tokens[1].Text);
        Assert.Equal((0, 2), (sink.Tokens[0].StartOffset, sink.Tokens[0].EndOffset));
        Assert.Equal((2, 4), (sink.Tokens[1].StartOffset, sink.Tokens[1].EndOffset));
    }

    [Fact(DisplayName = "ChineseLexiconTokeniser: Stop words applied correctly")]
    public void ChineseLexiconTokeniser_StopWords_Applied()
    {
        var analyser = new LanguageAnalyser(
            new ChineseLexiconTokeniser(new[] { "\u6211", "\u732B" }), // 我, 猫
            StopWords.Chinese,
            stemmer: null);
        var sink = new MaterialisingTokenSink();
        analyser.Analyse("\u6211\u7684\u732B", sink); // 我的猫
        // 的 is a stop word: should be filtered by StopWordFilter.
        // With stop words active, the stop word character is removed.
        Assert.True(sink.Tokens.Count <= 3, $"Expected at most 3 tokens (3 raw minus stop words), got {sink.Tokens.Count}");
        Assert.DoesNotContain(sink.Tokens, t => t.Text == "\u7684"); // 的 should not appear
    }
    #endregion

    #region Japanese tokeniser

    [Fact(DisplayName = "JapaneseTokeniser: Simple sentence segments")]
    public void JapaneseTokeniser_SimpleSentence_Segments()
    {
        using var tokeniser = new JapaneseTokeniser();
        var sink = new MaterialisingTokenSink();
        var input = "\u79C1\u306F\u5B66\u751F\u3067\u3059"; // 私は学生です

        tokeniser.Tokenise(input, sink);

        Assert.Equal(new[] { "\u79C1", "\u306F", "\u5B66\u751F", "\u3067\u3059" },
            sink.Tokens.Select(static token => token.Text));
        Assert.Equal(new[] { (0, 1), (1, 2), (2, 4), (4, 6) },
            sink.Tokens.Select(static token => (token.StartOffset, token.EndOffset)));
        Assert.All(sink.Tokens, static token => Assert.Equal(JapaneseTokeniser.JapaneseType, token.Type));
    }

    [Fact(DisplayName = "JapaneseTokeniser: Known word produces output")]
    public void JapaneseTokeniser_KnownWord_ProducesSingleToken()
    {
        using var tokeniser = new JapaneseTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("\u98DF\u3079\u308B", sink); // 食べる
        Assert.Equal(new[] { "\u98DF\u3079\u308B" }, sink.Tokens.Select(static token => token.Text));
    }

    [Fact(DisplayName = "JapaneseTokeniser: Empty string produces no tokens")]
    public void JapaneseTokeniser_EmptyString_NoTokens()
    {
        using var tokeniser = new JapaneseTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("", sink);
        Assert.Empty(sink.Tokens);
    }

    [Fact(DisplayName = "JapaneseTokeniser: ASCII path unaffected")]
    public void JapaneseTokeniser_AsciiOnly_StandardTokenisation()
    {
        using var tokeniser = new JapaneseTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("hello world", sink);
        Assert.Equal(2, sink.Tokens.Count);
        Assert.Equal("hello", sink.Tokens[0].Text);
        Assert.Equal("world", sink.Tokens[1].Text);
    }

    [Fact(DisplayName = "JapaneseTokeniser: Missing dictionary throws FileNotFoundException")]
    public void JapaneseTokeniser_MissingDictionary_ThrowsFileNotFoundException()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"kuromoji_nonexistent_{Guid.NewGuid():N}");
        Assert.Throws<FileNotFoundException>(() => new JapaneseTokeniser(nonExistentPath));
    }

    [Fact(DisplayName = "JapaneseTokeniser: Corrupt codec section is rejected")]
    public void JapaneseTokeniser_CorruptCodec_ThrowsInvalidDataException()
    {
        string path = Path.Combine(Path.GetTempPath(), $"japanese_corrupt_{Guid.NewGuid():N}.jlc");
        try
        {
            File.Copy(JapaneseTokeniser.DefaultDictionaryPath, path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                stream.Position = stream.Length - 1;
                int value = stream.ReadByte();
                stream.Position--;
                stream.WriteByte((byte)(value ^ 0xFF));
            }

            using var tokeniser = new JapaneseTokeniser(path);
            var sink = new MaterialisingTokenSink();
            Assert.Throws<InvalidDataException>(() => tokeniser.Tokenise("\u79C1", sink));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact(DisplayName = "JapaneseTokeniser: Truncated codec is rejected")]
    public void JapaneseTokeniser_TruncatedCodec_ThrowsInvalidDataException()
    {
        string path = Path.Combine(Path.GetTempPath(), $"japanese_truncated_{Guid.NewGuid():N}.jlc");
        try
        {
            File.WriteAllBytes(path, "JLC1"u8.ToArray());
            using var tokeniser = new JapaneseTokeniser(path);
            var sink = new MaterialisingTokenSink();
            Assert.Throws<InvalidDataException>(() => tokeniser.Tokenise("\u79C1", sink));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact(DisplayName = "JapaneseTokeniser: Warm tokenisation does not allocate")]
    public void JapaneseTokeniser_WarmPath_ZeroAllocation()
    {
        using var tokeniser = new JapaneseTokeniser();
        var sink = new CountingTokenSink();
        const string Input = "\u79C1\u306F\u5B66\u751F\u3067\u3059";

        for (int i = 0; i < 100; i++)
            tokeniser.Tokenise(Input, sink);
        sink.Reset();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            tokeniser.Tokenise(Input, sink);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(400, sink.Count);
        Assert.Equal(0, allocated);
    }

    [Fact(DisplayName = "JapaneseTokeniser: Concurrent calls remain independent")]
    public void JapaneseTokeniser_ConcurrentCalls_AreIndependent()
    {
        using var tokeniser = new JapaneseTokeniser();
        var results = new string[16][];

        Parallel.For(0, results.Length, i =>
        {
            var sink = new MaterialisingTokenSink();
            tokeniser.Tokenise("\u79C1\u306F\u5B66\u751F\u3067\u3059", sink);
            results[i] = sink.Tokens.Select(static token => token.Text).ToArray();
        });

        Assert.All(results, static tokens =>
            Assert.Equal(new[] { "\u79C1", "\u306F", "\u5B66\u751F", "\u3067\u3059" }, tokens));
    }

    #endregion

    #region AnalyserFactory wiring

    [Fact(DisplayName = "AnalyserFactory: Chinese uses lexicon-based tokeniser with stop words")]
    public void AnalyserFactory_Chinese_UsesChineseLexiconTokeniser()
    {
        var analyser = AnalyserFactory.Create("zh");
        var sink = new MaterialisingTokenSink();
        analyser.Analyse("\u6211\u7684\u732B", sink); // 我的猫
        Assert.Single(sink.Tokens);
        Assert.Equal("\u732B", sink.Tokens[0].Text);
    }

    [Fact(DisplayName = "AnalyserFactory: Chinese has stop words configured")]
    public void AnalyserFactory_Chinese_HasStopWords()
    {
        var analyser = AnalyserFactory.Create("zh");
        var sink = new MaterialisingTokenSink();
        analyser.Analyse("\u7684", sink); // 的 alone (Chinese stop word)
        Assert.Empty(sink.Tokens);
    }

    [Fact(DisplayName = "AnalyserFactory: Japanese uses JapaneseTokeniser")]
    public void AnalyserFactory_Japanese_UsesJapaneseTokeniser()
    {
        var analyser = AnalyserFactory.Create("ja");
        var sink = new MaterialisingTokenSink();
        analyser.Analyse("\u79C1\u306F\u5B66\u751F\u3067\u3059", sink); // 私は学生です
        Assert.Equal(new[] { "\u5B66\u751F" },
            sink.Tokens.Select(static token => token.Text));
    }

    [Fact(DisplayName = "AnalyserFactory: Korean uses fixed CJKBigramTokeniser")]
    public void AnalyserFactory_Korean_UsesFixedCJKBigramTokeniser()
    {
        var analyser = AnalyserFactory.Create("ko");
        var sink = new MaterialisingTokenSink();
        analyser.Analyse("\uD55C\uAD6D\uC5B4", sink); // 한국어 (Hangul)
        Assert.Single(sink.Tokens);
        Assert.Equal("\uD55C\uAD6D\uC5B4", sink.Tokens[0].Text); // 한국어
    }

    #endregion

    private sealed class CountingTokenSink : ISpanTokenSink
    {
        internal int Count { get; private set; }

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
        {
            Count++;
        }

        internal void Reset() => Count = 0;
    }
}

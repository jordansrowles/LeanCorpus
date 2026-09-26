using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Highlighting;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Contains unit tests for Query Parser.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class QueryParserTests
{
    private readonly QueryParser _parser = new("body", new StandardAnalyser());
    private readonly QueryParser _keywordParser = new("body", new KeywordAnalyser());

    /// <summary>
    /// Verifies the Parse: Single Term Returns Term Query scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Single Term Returns Term Query")]
    public void Parse_SingleTerm_ReturnsTermQuery()
    {
        var query = _parser.Parse("corpus");
        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("body", tq.Field);
        Assert.Equal("corpus", tq.Term);
    }

    /// <summary>
    /// Verifies the Parse: Field Colon Term Returns Term Query With Field scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Field Colon Term Returns Term Query With Field")]
    public void Parse_FieldColonTerm_ReturnsTermQueryWithField()
    {
        var query = _parser.Parse("title:search");
        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("title", tq.Field);
        Assert.Equal("search", tq.Term);
    }

    [Fact(DisplayName = "Parse: Explicit field term uses its resolved analyser")]
    public void Parse_ExplicitFieldTerm_UsesResolvedAnalyser()
    {
        var query = Assert.IsType<TermQuery>(CreateFieldAwareParser().Parse("exactText:ABC-123"));

        Assert.Equal("exactText", query.Field);
        Assert.Equal("ABC-123", query.Term);
    }

    [Fact(DisplayName = "Parse: Explicit field phrase and fuzzy term use their resolved analyser")]
    public void Parse_ExplicitFieldPhraseAndFuzzy_UseResolvedAnalyser()
    {
        QueryParser parser = CreateFieldAwareParser();

        var phrase = Assert.IsType<PhraseQuery>(parser.Parse("exactText:\"ABC-123\""));
        var fuzzy = Assert.IsType<FuzzyQuery>(parser.Parse("exactText:ABC-123~1"));

        Assert.Equal(new[] { "ABC-123" }, phrase.Terms);
        Assert.Equal("ABC-123", fuzzy.Term);
    }

    [Fact(DisplayName = "Parse: Explicit field wildcard and range use their resolved normaliser")]
    public void Parse_ExplicitFieldWildcardAndRange_UseResolvedNormaliser()
    {
        QueryParser parser = CreateFieldAwareParser();

        var prefix = Assert.IsType<PrefixQuery>(parser.Parse("exactText:ABC-123*"));
        var range = Assert.IsType<TermRangeQuery>(parser.Parse("exactText:[ABC-123 TO XYZ-999]"));

        Assert.Equal("ABC-123", prefix.Prefix);
        Assert.Equal("ABC-123", range.LowerTerm);
        Assert.Equal("XYZ-999", range.UpperTerm);
    }

    [Fact(DisplayName = "Parse: Explicit field regexp remains attached to its resolved field")]
    public void Parse_ExplicitFieldRegexp_UsesResolvedFieldContext()
    {
        var query = Assert.IsType<RegexpQuery>(CreateFieldAwareParser().Parse("exactText:/ABC-123/"));

        Assert.Equal("exactText", query.Field);
        Assert.Equal("ABC-123", query.Pattern);
    }

    /// <summary>
    /// Verifies the Parse: Quoted Phrase Returns Phrase Query scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Quoted Phrase Returns Phrase Query")]
    public void Parse_QuotedPhrase_ReturnsPhraseQuery()
    {
        var query = _parser.Parse("\"quick brown fox\"");
        var pq = Assert.IsType<PhraseQuery>(query);
        Assert.Equal("body", pq.Field);
        Assert.Equal(new[] { "quick", "brown", "fox" }, pq.Terms);
    }

    /// <summary>
    /// Verifies the Parse: Required Term Returns Must Clause scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Required Term Returns Must Clause")]
    public void Parse_RequiredTerm_ReturnsMustClause()
    {
        var query = _parser.Parse("+required");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Single(bq.Clauses);
        Assert.Equal(Occur.Must, bq.Clauses[0].Occur);
    }

    /// <summary>
    /// Verifies the Parse: Excluded Term Returns Must Not Clause scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Excluded Term Returns Must Not Clause")]
    public void Parse_ExcludedTerm_ReturnsMustNotClause()
    {
        var query = _parser.Parse("-excluded");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Single(bq.Clauses);
        Assert.Equal(Occur.MustNot, bq.Clauses[0].Occur);
    }

    /// <summary>
    /// Verifies the Parse: Multiple Terms Returns Boolean With Should Clauses scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Multiple Terms Returns Boolean With Should Clauses")]
    public void Parse_MultipleTerms_ReturnsBooleanWithShouldClauses()
    {
        var query = _parser.Parse("quick brown fox");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Equal(3, bq.Clauses.Count);
        Assert.All(bq.Clauses, c => Assert.Equal(Occur.Should, c.Occur));
    }

    [Fact(DisplayName = "Parse: Unquoted analysis preserves every token from one syntax term")]
    public void Parse_UnquotedAnalyserOutput_PreservesEveryToken()
    {
        var query = _parser.Parse("foo-bar");

        var boolean = Assert.IsType<BooleanQuery>(query);
        Assert.Equal(2, boolean.Clauses.Count);
        Assert.All(boolean.Clauses, static clause => Assert.Equal(Occur.Should, clause.Occur));
        Assert.Equal(
            new[] { "foo", "bar" },
            boolean.Clauses.Select(static clause => Assert.IsType<TermQuery>(clause.Query).Term));
    }

    [Fact(DisplayName = "Parse: Unquoted same-position analyser alternatives are preserved")]
    public void Parse_UnquotedSamePositionAlternatives_ArePreserved()
    {
        var parser = new QueryParser("body", new DelegateAnalyser(static sink =>
        {
            sink.Add("quick".AsSpan(), 0, 5, Token.DefaultType, 1, 1, null);
            sink.Add("fast".AsSpan(), 0, 4, Token.DefaultType, 0, 1, null);
        }));

        var boolean = Assert.IsType<BooleanQuery>(parser.Parse("alias"));

        Assert.Equal(2, boolean.Clauses.Count);
        Assert.All(boolean.Clauses, static clause => Assert.Equal(Occur.Should, clause.Occur));
        Assert.Equal(new[] { "quick", "fast" },
            boolean.Clauses.Select(static clause => Assert.IsType<TermQuery>(clause.Query).Term));
    }

    [Fact(DisplayName = "Parse: Unquoted analyser graph compiles complete bounded paths")]
    public void Parse_UnquotedAnalyserGraph_CompilesCompletePaths()
    {
        var parser = new QueryParser("body", new DelegateAnalyser(EmitHyphenatedSynonymGraph));

        var boolean = Assert.IsType<BooleanQuery>(parser.Parse("new-york"));

        Assert.Equal(2, boolean.Clauses.Count);
        var paths = boolean.Clauses
            .Select(static clause => Assert.IsType<PhraseQuery>(clause.Query))
            .ToArray();
        Assert.Contains(paths, static phrase =>
            phrase.Terms.SequenceEqual(new[] { "new", "york" }) &&
            phrase.Positions.SequenceEqual(new[] { 0, 1 }));
        Assert.Contains(paths, static phrase =>
            phrase.Terms.SequenceEqual(new[] { "nyc" }) &&
            phrase.Positions.SequenceEqual(new[] { 0 }));
    }

    [Fact(DisplayName = "Parse: Unquoted analyser graph obeys configured path limit")]
    public void Parse_UnquotedAnalyserGraph_ObeysConfiguredPathLimit()
    {
        var parser = new QueryParser(
            "body", new DelegateAnalyser(EmitHyphenatedSynonymGraph), maxGraphPaths: 1);

        var exception = Assert.Throws<QueryParseException>(() => parser.Parse("new-york"));

        Assert.Contains("configured maximum of 1 paths", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Parse: Unquoted term with no analysed tokens emits no clause")]
    public void Parse_UnquotedTermWithNoAnalysedTokens_EmitsNoClause()
    {
        var parser = new QueryParser("body", new DelegateAnalyser(static _ => { }));

        var boolean = Assert.IsType<BooleanQuery>(parser.Parse("removed"));

        Assert.Empty(boolean.Clauses);
    }

    [Fact(DisplayName = "Parse: Fuzzy syntax preserves multiple linear analysis outputs")]
    public void Parse_FuzzyTermWithMultipleAnalysisOutputs_PreservesEachOutput()
    {
        var boolean = Assert.IsType<BooleanQuery>(_parser.Parse("foo-bar~1"));

        Assert.Equal(2, boolean.Clauses.Count);
        Assert.All(boolean.Clauses, static clause => Assert.Equal(Occur.Should, clause.Occur));
        Assert.Equal(new[] { "foo", "bar" }, boolean.Clauses
            .Select(static clause => Assert.IsType<FuzzyQuery>(clause.Query).Term));
    }

    [Fact(DisplayName = "Parse: Analysed wildcard literal rejects multiple tokens explicitly")]
    public void Parse_AnalysedWildcardLiteralWithMultipleTokens_ThrowsInsteadOfTruncating()
    {
        var parser = new AnalysingQueryParser("body", new StandardAnalyser());

        var exception = Assert.Throws<QueryParseException>(() => parser.Parse("foo-bar*"));

        Assert.Contains("must analyse to at most one unit-length token", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies the Parse: Prefix Wildcard Returns Prefix Query scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Prefix Wildcard Returns Prefix Query")]
    public void Parse_PrefixWildcard_ReturnsPrefixQuery()
    {
        var query = _parser.Parse("search*");
        var pq = Assert.IsType<PrefixQuery>(query);
        Assert.Equal("body", pq.Field);
        Assert.Equal("search", pq.Prefix);
    }

    /// <summary>
    /// Verifies the Parse: Wildcard Pattern Returns Wildcard Query scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Wildcard Pattern Returns Wildcard Query")]
    public void Parse_WildcardPattern_ReturnsWildcardQuery()
    {
        var query = _parser.Parse("te?t");
        var wq = Assert.IsType<WildcardQuery>(query);
        Assert.Equal("body", wq.Field);
        Assert.Equal("te?t", wq.Pattern);
    }

    [Theory(DisplayName = "Parse: Escaped wildcard characters remain literal terms")]
    [InlineData(@"foo\*bar", "foo*bar")]
    [InlineData(@"foo\?bar", "foo?bar")]
    public void Parse_EscapedWildcards_AreLiteralTerms(string input, string expectedTerm)
    {
        var query = Assert.IsType<TermQuery>(_keywordParser.Parse(input));

        Assert.Equal(expectedTerm, query.Term);
    }

    [Fact(DisplayName = "Parse: Mixed wildcard pattern retains escaped literal metacharacters")]
    public void Parse_MixedWildcardPattern_PreservesEscapedStar()
    {
        var query = Assert.IsType<WildcardQuery>(_keywordParser.Parse(@"foo\**bar"));

        Assert.Equal(@"foo\**bar", query.Pattern);
        Assert.True(WildcardQuery.Matches("foo*bar", query.Pattern));
        Assert.False(WildcardQuery.Matches("fooxbar", query.Pattern));
    }

    [Fact(DisplayName = "Parse: Analysed mixed wildcard pattern normalises literals without activating escapes")]
    public void Parse_AnalysedMixedWildcardPattern_PreservesEscapedStar()
    {
        var parser = new AnalysingQueryParser("body", new StandardAnalyser());

        var query = Assert.IsType<WildcardQuery>(parser.Parse(@"Foo\**Bar"));

        Assert.Equal(@"foo\**bar", query.Pattern);
        Assert.True(WildcardQuery.Matches("foo*bar", query.Pattern));
    }

    [Fact(DisplayName = "Parse: Escaped wildcard before a trailing star becomes a literal prefix")]
    public void Parse_EscapedWildcardPrefix_UsesLiteralPrefix()
    {
        var query = Assert.IsType<PrefixQuery>(_keywordParser.Parse(@"foo\**"));

        Assert.Equal("foo*", query.Prefix);
    }

    [Fact(DisplayName = "Parse: Escaped quote inside a phrase remains literal content")]
    public void Parse_EscapedQuoteInsidePhrase_IsLiteralContent()
    {
        var query = Assert.IsType<PhraseQuery>(_keywordParser.Parse("\"foo\\\"bar\""));

        Assert.Equal(new[] { "foo\"bar" }, query.Terms);
    }

    [Fact(DisplayName = "Parse: Escaped wildcard range bound remains literal")]
    public void Parse_EscapedWildcardRangeBound_RemainsLiteral()
    {
        var query = Assert.IsType<TermRangeQuery>(_keywordParser.Parse(@"body:[\* TO z]"));

        Assert.Equal("*", query.LowerTerm);
        Assert.Equal("z", query.UpperTerm);
    }

    [Theory(DisplayName = "Parse: Regex escaping preserves regex operators and quoted delimiters")]
    [InlineData(@"/foo\/bar/", "foo/bar")]
    [InlineData(@"/\d+/", @"\d+")]
    public void Parse_RegexEscaping_PreservesRegexOperators(string input, string expectedPattern)
    {
        var query = Assert.IsType<RegexpQuery>(_keywordParser.Parse(input));

        Assert.Equal(expectedPattern, query.Pattern);
    }

    /// <summary>
    /// Verifies the Parse: Fuzzy Term Returns Fuzzy Query scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Fuzzy Term Returns Fuzzy Query")]
    public void Parse_FuzzyTerm_ReturnsFuzzyQuery()
    {
        var query = _parser.Parse("corpus~2");
        var fq = Assert.IsType<FuzzyQuery>(query);
        Assert.Equal("body", fq.Field);
        Assert.Equal("corpus", fq.Term);
        Assert.Equal(2, fq.MaxEdits);
    }

    /// <summary>
    /// Verifies parsing rejects unsupported fuzzy edit distances at the modifier offset.
    /// </summary>
    [Theory(DisplayName = "Parse: Unsupported Fuzzy Edit Distance Reports Modifier Offset")]
    [InlineData("corpus~3", 6)]
    [InlineData("corpus~1000", 6)]
    public void Parse_UnsupportedFuzzyEditDistance_ThrowsAtModifierOffset(string queryText, int expectedOffset)
    {
        var exception = Assert.Throws<QueryParseException>(() => _parser.Parse(queryText));

        Assert.Equal(expectedOffset, exception.Offset);
    }

    /// <summary>
    /// Verifies the Parse: Phrase With Slop Returns Phrase Query With Slop scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Phrase With Slop Returns Phrase Query With Slop")]
    public void Parse_PhraseWithSlop_ReturnsPhraseQueryWithSlop()
    {
        var query = _parser.Parse("\"quick fox\"~2");
        var pq = Assert.IsType<PhraseQuery>(query);
        Assert.Equal(2, pq.Slop);
    }

    /// <summary>
    /// Verifies the Parse: Boost Suffix Sets Boost On Query scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Boost Suffix Sets Boost On Query")]
    public void Parse_BoostSuffix_SetsBoostOnQuery()
    {
        var query = _parser.Parse("important^3.5");
        Assert.Equal(3.5f, query.Boost, 0.01f);
    }

    /// <summary>
    /// Verifies the Parse: Empty String Returns Boolean Query With No Clauses scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Empty String Returns Boolean Query With No Clauses")]
    public void Parse_EmptyString_ReturnsBooleanQueryWithNoClauses()
    {
        var query = _parser.Parse("");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Empty(bq.Clauses);
    }

    /// <summary>
    /// Verifies the Parse: Grouped Parens Returns Nested Boolean Query scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Grouped Parens Returns Nested Boolean Query")]
    public void Parse_GroupedParens_ReturnsNestedBooleanQuery()
    {
        var query = _parser.Parse("+(quick brown)");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Single(bq.Clauses);
        Assert.Equal(Occur.Must, bq.Clauses[0].Occur);
        Assert.IsType<BooleanQuery>(bq.Clauses[0].Query);
    }

    /// <summary>
    /// Verifies the Parse: Field Colon Phrase Returns Phrase Query On Field scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Field Colon Phrase Returns Phrase Query On Field")]
    public void Parse_FieldColonPhrase_ReturnsPhraseQueryOnField()
    {
        var query = _parser.Parse("title:\"exact match\"");
        var pq = Assert.IsType<PhraseQuery>(query);
        Assert.Equal("title", pq.Field);
    }

    /// <summary>
    /// Verifies the Parse: Mixed Clauses Correct Occur Types scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Mixed Clauses Correct Occur Types")]
    public void Parse_MixedClauses_CorrectOccurTypes()
    {
        var query = _parser.Parse("+required optional -excluded");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Equal(3, bq.Clauses.Count);
        Assert.Equal(Occur.Must, bq.Clauses[0].Occur);
        Assert.Equal(Occur.Should, bq.Clauses[1].Occur);
        Assert.Equal(Occur.MustNot, bq.Clauses[2].Occur);
    }

    [Fact(DisplayName = "Parse: Explicit Boolean Operators Honour Precedence")]
    public void Parse_ExplicitBooleanOperators_HonourPrecedence()
    {
        var query = _parser.Parse("alpha OR beta AND gamma");
        var outer = Assert.IsType<BooleanQuery>(query);
        Assert.Equal(2, outer.Clauses.Count);
        Assert.IsType<TermQuery>(outer.Clauses[0].Query);

        var conjunction = Assert.IsType<BooleanQuery>(outer.Clauses[1].Query);
        Assert.Equal(2, conjunction.Clauses.Count);
        Assert.All(conjunction.Clauses, static clause => Assert.Equal(Occur.Must, clause.Occur));
    }

    [Fact(DisplayName = "Parse: Explicit Not Creates Prohibited Clause")]
    public void Parse_ExplicitNot_CreatesProhibitedClause()
    {
        var query = _parser.Parse("alpha NOT beta");
        var boolean = Assert.IsType<BooleanQuery>(query);

        Assert.Equal(Occur.Must, boolean.Clauses[0].Occur);
        Assert.Equal(Occur.MustNot, boolean.Clauses[1].Occur);
    }

    [Theory(DisplayName = "Parse: Text Range Creates Term Range Query")]
    [InlineData("title:[alpha TO omega]", true, true)]
    [InlineData("title:{alpha TO omega}", false, false)]
    [InlineData("title:[* TO omega}", true, false)]
    public void Parse_TextRange_CreatesTermRangeQuery(
        string text, bool includeLower, bool includeUpper)
    {
        var query = Assert.IsType<TermRangeQuery>(_parser.Parse(text));

        Assert.Equal("title", query.Field);
        Assert.Equal(includeLower, query.IncludeLower);
        Assert.Equal(includeUpper, query.IncludeUpper);
        if (text.Contains('*'))
            Assert.Null(query.LowerTerm);
    }

    [Fact(DisplayName = "Parse: Field Exists Creates Field Exists Query")]
    public void Parse_FieldExists_CreatesFieldExistsQuery()
    {
        var query = Assert.IsType<FieldExistsQuery>(_parser.Parse("_exists_:title"));

        Assert.Equal("title", query.Field);
    }

    [Fact(DisplayName = "Parse: Regular Expression Creates Regexp Query")]
    public void Parse_RegularExpression_CreatesRegexpQuery()
    {
        var query = Assert.IsType<RegexpQuery>(_parser.Parse(@"title:/cor(pus|pora)/"));

        Assert.Equal("title", query.Field);
        Assert.Equal("cor(pus|pora)", query.Pattern);
    }

    [Fact(DisplayName = "Parse: Constant Score Syntax Wraps Query")]
    public void Parse_ConstantScoreSyntax_WrapsQuery()
    {
        var query = Assert.IsType<ConstantScoreQuery>(_parser.Parse("important^=2.5"));

        Assert.Equal(2.5f, query.ConstantScore);
        Assert.IsType<TermQuery>(query.Inner);
    }

    [Fact(DisplayName = "Parse: Pipe Group Creates Disjunction Max Query")]
    public void Parse_PipeGroup_CreatesDisjunctionMaxQuery()
    {
        var query = Assert.IsType<DisjunctionMaxQuery>(_parser.Parse("(alpha | beta | gamma)"));

        Assert.Equal(3, query.Disjuncts.Count);
    }

    /// <summary>
    /// Verifies the Parse: Invalid Syntax Throws Query Parse Exception scenario.
    /// </summary>
    /// <param name="query">The query value for the test case.</param>
    [Theory(DisplayName = "Parse: Invalid Syntax Throws Query Parse Exception")]
    [InlineData("\"unterminated")]
    [InlineData("(quick brown")]
    [InlineData("title:")]
    [InlineData("+")]
    [InlineData("quick)")]
    [InlineData("alpha AND")]
    [InlineData("title:[alpha omega]")]
    [InlineData("title:/unterminated")]
    public void Parse_InvalidSyntax_ThrowsQueryParseException(string query)
    {
        Assert.Throws<QueryParseException>(() => _parser.Parse(query));
    }

    /// <summary>
    /// Verifies that excessive nesting depth throws QueryParseException.
    /// </summary>
    [Fact(DisplayName = "Parse: Excessive Nesting Depth Throws Query Parse Exception")]
    public void Parse_ExcessiveNestingDepth_ThrowsQueryParseException()
    {
        var deep = new string('(', 65) + "term" + new string(')', 65);
        Assert.Throws<QueryParseException>(() => _parser.Parse(deep));
    }

    // ═══════════════════════════════════════════════════
    //  Backslash escaping
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Parse: Escaped colon in field value returns correct term")]
    public void Parse_EscapedColon_ReturnsTermWithColon()
    {
        var query = _keywordParser.Parse(@"url:https\://example.com/path");
        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("url", tq.Field);
        Assert.Equal("https://example.com/path", tq.Term);
    }

    [Fact(DisplayName = "Parse: Multiple escaped colons in timestamp")]
    public void Parse_EscapedTimestamp_ReturnsTermWithColons()
    {
        var query = _keywordParser.Parse(@"ts:2024\:01\:15T10\:30\:00");
        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("ts", tq.Field);
        Assert.Equal("2024:01:15T10:30:00", tq.Term);
    }

    [Fact(DisplayName = "Parse: Escaped colon and backslash in file path")]
    public void Parse_EscapedFilePath_ReturnsTermWithBackslashAndColon()
    {
        var query = _keywordParser.Parse(@"path:C\:\\Users\\foo.txt");
        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("path", tq.Field);
        Assert.Equal(@"C:\Users\foo.txt", tq.Term);
    }

    [Fact(DisplayName = "Parse: Escaped colon in unfielded term")]
    public void Parse_EscapedColon_Unfielded_ReturnsTermWithColon()
    {
        var query = _keywordParser.Parse(@"hello\:world");
        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("body", tq.Field);
        Assert.Equal("hello:world", tq.Term);
    }

    [Fact(DisplayName = "Parse: Escaped operators become literal")]
    public void Parse_EscapedOperators_ReturnLiteral()
    {
        var query = _keywordParser.Parse(@"a\+b\-c\(d\)e\~f\^g\""h");
        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("a+b-c(d)e~f^g\"h", tq.Term);
    }

    [Fact(DisplayName = "Parse: No escapes uses span fast path")]
    public void Parse_NoEscapes_StillWorks()
    {
        var query = _parser.Parse("normal:term");
        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("normal", tq.Field);
        Assert.Equal("term", tq.Term);
    }

    private static void EmitHyphenatedSynonymGraph(ISpanTokenSink sink)
    {
        sink.Add("new".AsSpan(), 0, 3, Token.DefaultType, 1, 1, null);
        sink.Add("nyc".AsSpan(), 0, 8, Token.DefaultType, 0, 2, null);
        sink.Add("york".AsSpan(), 4, 8, Token.DefaultType, 1, 1, null);
    }

    private sealed class DelegateAnalyser(Action<ISpanTokenSink> emit) : IAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink) => emit(sink);
    }

    private static QueryParser CreateFieldAwareParser() => new(
        "body",
        new StandardAnalyser(),
        static field =>
        {
            IAnalyser analyser = string.Equals(field, "exactText", StringComparison.Ordinal)
                ? new KeywordAnalyser()
                : new StandardAnalyser();
            return new QueryFieldCompilationContext(
                field,
                analyser,
                QueryParser.CreateSingleTokenNormaliser(analyser));
        });
}

using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Parsing;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Unit tests for query model types: equality, hash codes, property accessors,
/// builder/freeze behaviour, and argument validation.
/// </summary>
[Trait("Category", "QueryModel")]
[Trait("Category", "UnitTest")]
public sealed class QueryModelTests
{
    // ── DisjunctionMaxQuery ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Disjunction Max Query: Field Is Empty When No Disjuncts Added scenario.
    /// </summary>
    [Fact(DisplayName = "Disjunction Max Query: Field Is Empty When No Disjuncts Added")]
    public void DisjunctionMaxQuery_Field_IsEmpty_WhenNoDisjunctsAdded()
    {
        var q = new DisjunctionMaxQuery();
        Assert.Equal(string.Empty, q.Field);
    }

    /// <summary>
    /// Verifies the Disjunction Max Query: Field Returns First Disjunct Field scenario.
    /// </summary>
    [Fact(DisplayName = "Disjunction Max Query: Field Returns First Disjunct Field")]
    public void DisjunctionMaxQuery_Field_ReturnsFirstDisjunctField()
    {
        var q = new DisjunctionMaxQuery();
        q.Add(new TermQuery("title", "foo"));
        q.Add(new TermQuery("body", "foo"));
        Assert.Equal("title", q.Field);
    }

    /// <summary>
    /// Verifies the Disjunction Max Query: Add Returns This For Chaining scenario.
    /// </summary>
    [Fact(DisplayName = "Disjunction Max Query: Add Returns This For Chaining")]
    public void DisjunctionMaxQuery_Add_ReturnsThisForChaining()
    {
        var q = new DisjunctionMaxQuery(0.1f);
        var returned = q.Add(new TermQuery("f", "t"));
        Assert.Same(q, returned);
    }

    /// <summary>
    /// Verifies the Disjunction Max Query: Add Throws On Null Query scenario.
    /// </summary>
    [Fact(DisplayName = "Disjunction Max Query: Add Throws On Null Query")]
    public void DisjunctionMaxQuery_Add_ThrowsOnNullQuery()
    {
        var q = new DisjunctionMaxQuery();
        Assert.Throws<ArgumentNullException>(() => q.Add(null!));
    }

    /// <summary>
    /// Verifies the Disjunction Max Query: Freeze Prevents Further Adds scenario.
    /// </summary>
    [Fact(DisplayName = "Disjunction Max Query: Freeze Prevents Further Adds")]
    public void DisjunctionMaxQuery_Freeze_PreventsFurtherAdds()
    {
        var q = new DisjunctionMaxQuery();
        q.Add(new TermQuery("f", "t"));
        q.Freeze();
        Assert.Throws<InvalidOperationException>(() => q.Add(new TermQuery("f", "x")));
    }

    /// <summary>
    /// Verifies the Disjunction Max Query: Freeze Returns This scenario.
    /// </summary>
    [Fact(DisplayName = "Disjunction Max Query: Freeze Returns This")]
    public void DisjunctionMaxQuery_Freeze_ReturnsThis()
    {
        var q = new DisjunctionMaxQuery();
        var returned = q.Freeze();
        Assert.Same(q, returned);
    }

    /// <summary>
    /// Verifies the Disjunction Max Query: Equals Same Disjuncts And Tie Breaker scenario.
    /// </summary>
    [Fact(DisplayName = "Disjunction Max Query: Equals Same Disjuncts And Tie Breaker")]
    public void DisjunctionMaxQuery_Equals_SameDisjunctsAndTieBreaker()
    {
        var a = new DisjunctionMaxQuery(0.3f);
        a.Add(new TermQuery("f", "x"));
        var b = new DisjunctionMaxQuery(0.3f);
        b.Add(new TermQuery("f", "x"));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>
    /// Verifies the Disjunction Max Query: Not Equals Different Tie Breaker scenario.
    /// </summary>
    [Fact(DisplayName = "Disjunction Max Query: Not Equals Different Tie Breaker")]
    public void DisjunctionMaxQuery_NotEquals_DifferentTieBreaker()
    {
        var a = new DisjunctionMaxQuery(0.1f);
        a.Add(new TermQuery("f", "x"));
        var b = new DisjunctionMaxQuery(0.5f);
        b.Add(new TermQuery("f", "x"));

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// Verifies the Disjunction Max Query: Not Equals Different Number Of Disjuncts scenario.
    /// </summary>
    [Fact(DisplayName = "Disjunction Max Query: Not Equals Different Number Of Disjuncts")]
    public void DisjunctionMaxQuery_NotEquals_DifferentNumberOfDisjuncts()
    {
        var a = new DisjunctionMaxQuery();
        a.Add(new TermQuery("f", "x"));

        var b = new DisjunctionMaxQuery();
        b.Add(new TermQuery("f", "x"));
        b.Add(new TermQuery("f", "y"));

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// Verifies the Disjunction Max Query: Builder Produces Correct Query scenario.
    /// </summary>
    [Fact(DisplayName = "Disjunction Max Query: Builder Produces Correct Query")]
    public void DisjunctionMaxQuery_Builder_ProducesCorrectQuery()
    {
        var q = new DisjunctionMaxQuery.Builder()
            .WithTieBreakerMultiplier(0.2f)
            .Add(new TermQuery("title", "foo"))
            .Add(new TermQuery("body", "foo"))
            .Build();

        Assert.Equal(0.2f, q.TieBreakerMultiplier);
        Assert.Equal(2, q.Disjuncts.Count);
    }

    /// <summary>
    /// Verifies the Disjunction Max Query: Builder Add Throws On Null scenario.
    /// </summary>
    [Fact(DisplayName = "Disjunction Max Query: Builder Add Throws On Null")]
    public void DisjunctionMaxQuery_Builder_AddThrowsOnNull()
    {
        var builder = new DisjunctionMaxQuery.Builder();
        Assert.Throws<ArgumentNullException>(() => builder.Add(null!));
    }

    // ── ConstantScoreQuery ───────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Constant Score Query: Field Delegates To Inner scenario.
    /// </summary>
    [Fact(DisplayName = "Constant Score Query: Field Delegates To Inner")]
    public void ConstantScoreQuery_Field_DelegatesToInner()
    {
        var q = new ConstantScoreQuery(new TermQuery("body", "hello"), 2.5f);
        Assert.Equal("body", q.Field);
    }

    /// <summary>
    /// Verifies the Constant Score Query: Constructor Throws On Null Inner scenario.
    /// </summary>
    [Fact(DisplayName = "Constant Score Query: Constructor Throws On Null Inner")]
    public void ConstantScoreQuery_Constructor_ThrowsOnNullInner()
    {
        Assert.Throws<ArgumentNullException>(() => new ConstantScoreQuery(null!));
    }

    /// <summary>
    /// Verifies the Constant Score Query: Equals Same Inner And Score scenario.
    /// </summary>
    [Fact(DisplayName = "Constant Score Query: Equals Same Inner And Score")]
    public void ConstantScoreQuery_Equals_SameInnerAndScore()
    {
        var a = new ConstantScoreQuery(new TermQuery("f", "t"), 3.0f);
        var b = new ConstantScoreQuery(new TermQuery("f", "t"), 3.0f);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>
    /// Verifies the Constant Score Query: Not Equals Different Score scenario.
    /// </summary>
    [Fact(DisplayName = "Constant Score Query: Not Equals Different Score")]
    public void ConstantScoreQuery_NotEquals_DifferentScore()
    {
        var a = new ConstantScoreQuery(new TermQuery("f", "t"), 1.0f);
        var b = new ConstantScoreQuery(new TermQuery("f", "t"), 2.0f);

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// Verifies the Constant Score Query: Not Equals Different Boost scenario.
    /// </summary>
    [Fact(DisplayName = "Constant Score Query: Not Equals Different Boost")]
    public void ConstantScoreQuery_NotEquals_DifferentBoost()
    {
        var a = new ConstantScoreQuery(new TermQuery("f", "t")) { Boost = 1.0f };
        var b = new ConstantScoreQuery(new TermQuery("f", "t")) { Boost = 2.0f };

        Assert.NotEqual(a, b);
    }

    // ── BlockJoinQuery ───────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Block Join Query: Field Delegates To Child Query scenario.
    /// </summary>
    [Fact(DisplayName = "Block Join Query: Field Delegates To Child Query")]
    public void BlockJoinQuery_Field_DelegatesToChildQuery()
    {
        var q = new BlockJoinQuery(new TermQuery("category", "shoes"));
        Assert.Equal("category", q.Field);
    }

    /// <summary>
    /// Verifies the Block Join Query: Constructor Throws On Null Child scenario.
    /// </summary>
    [Fact(DisplayName = "Block Join Query: Constructor Throws On Null Child")]
    public void BlockJoinQuery_Constructor_ThrowsOnNullChild()
    {
        Assert.Throws<ArgumentNullException>(() => new BlockJoinQuery(null!));
    }

    /// <summary>
    /// Verifies the Block Join Query: Equals Same Child Query scenario.
    /// </summary>
    [Fact(DisplayName = "Block Join Query: Equals Same Child Query")]
    public void BlockJoinQuery_Equals_SameChildQuery()
    {
        var a = new BlockJoinQuery(new TermQuery("cat", "shoes"));
        var b = new BlockJoinQuery(new TermQuery("cat", "shoes"));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>
    /// Verifies the Block Join Query: Not Equals Different Child scenario.
    /// </summary>
    [Fact(DisplayName = "Block Join Query: Not Equals Different Child")]
    public void BlockJoinQuery_NotEquals_DifferentChild()
    {
        var a = new BlockJoinQuery(new TermQuery("cat", "shoes"));
        var b = new BlockJoinQuery(new TermQuery("cat", "boots"));

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// Verifies the Block Join Query: ToString Contains Child Query scenario.
    /// </summary>
    [Fact(DisplayName = "Block Join Query: ToString Contains Child Query")]
    public void BlockJoinQuery_ToString_ContainsChildQuery()
    {
        var q = new BlockJoinQuery(new TermQuery("cat", "shoes"));
        var str = q.ToString();
        Assert.Contains("BlockJoinQuery", str);
    }

    // ── MoreLikeThisQuery ────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the More Like This Query: Field Is Empty When No Fields scenario.
    /// </summary>
    [Fact(DisplayName = "More Like This Query: Field Is Empty When No Fields")]
    public void MoreLikeThisQuery_Field_IsEmpty_WhenNoFields()
    {
        var q = new MoreLikeThisQuery(0, []);
        Assert.Equal(string.Empty, q.Field);
    }

    /// <summary>
    /// Verifies the More Like This Query: Field Returns First Field scenario.
    /// </summary>
    [Fact(DisplayName = "More Like This Query: Field Returns First Field")]
    public void MoreLikeThisQuery_Field_ReturnsFirstField()
    {
        var q = new MoreLikeThisQuery(5, ["body", "title"]);
        Assert.Equal("body", q.Field);
    }

    /// <summary>
    /// Verifies the More Like This Query: Default Parameters When Null scenario.
    /// </summary>
    [Fact(DisplayName = "More Like This Query: Default Parameters When Null")]
    public void MoreLikeThisQuery_DefaultParameters_WhenNull()
    {
        var q = new MoreLikeThisQuery(1, ["body"], null);
        Assert.NotNull(q.Parameters);
    }

    /// <summary>
    /// Verifies the More Like This Query: Equals Same Doc And Fields scenario.
    /// </summary>
    [Fact(DisplayName = "More Like This Query: Equals Same Doc And Fields")]
    public void MoreLikeThisQuery_Equals_SameDocAndFields()
    {
        var a = new MoreLikeThisQuery(42, ["body", "title"]);
        var b = new MoreLikeThisQuery(42, ["body", "title"]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>
    /// Verifies the More Like This Query: Not Equals Different Doc Id scenario.
    /// </summary>
    [Fact(DisplayName = "More Like This Query: Not Equals Different Doc Id")]
    public void MoreLikeThisQuery_NotEquals_DifferentDocId()
    {
        var a = new MoreLikeThisQuery(1, ["body"]);
        var b = new MoreLikeThisQuery(2, ["body"]);

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// Verifies the More Like This Query: Not Equals Different Fields scenario.
    /// </summary>
    [Fact(DisplayName = "More Like This Query: Not Equals Different Fields")]
    public void MoreLikeThisQuery_NotEquals_DifferentFields()
    {
        var a = new MoreLikeThisQuery(1, ["body"]);
        var b = new MoreLikeThisQuery(1, ["title"]);

        Assert.NotEqual(a, b);
    }

    // ── SpanTermQuery ────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Span Term Query: Properties Set Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Span Term Query: Properties Set Correctly")]
    public void SpanTermQuery_Properties_SetCorrectly()
    {
        var q = new SpanTermQuery("content", "elastic");
        Assert.Equal("content", q.Field);
        Assert.Equal("elastic", q.Term);
    }

    /// <summary>
    /// Verifies the Span Term Query: Equals Same Field And Term scenario.
    /// </summary>
    [Fact(DisplayName = "Span Term Query: Equals Same Field And Term")]
    public void SpanTermQuery_Equals_SameFieldAndTerm()
    {
        var a = new SpanTermQuery("f", "hello");
        var b = new SpanTermQuery("f", "hello");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>
    /// Verifies the Span Term Query: Not Equals Different Term scenario.
    /// </summary>
    [Fact(DisplayName = "Span Term Query: Not Equals Different Term")]
    public void SpanTermQuery_NotEquals_DifferentTerm()
    {
        var a = new SpanTermQuery("f", "hello");
        var b = new SpanTermQuery("f", "world");

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// Verifies the Span Term Query: Not Equals Different Field scenario.
    /// </summary>
    [Fact(DisplayName = "Span Term Query: Not Equals Different Field")]
    public void SpanTermQuery_NotEquals_DifferentField()
    {
        var a = new SpanTermQuery("title", "foo");
        var b = new SpanTermQuery("body", "foo");

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// Verifies the Span Term Query: Not Equals Different Boost scenario.
    /// </summary>
    [Fact(DisplayName = "Span Term Query: Not Equals Different Boost")]
    public void SpanTermQuery_NotEquals_DifferentBoost()
    {
        var a = new SpanTermQuery("f", "t") { Boost = 1.0f };
        var b = new SpanTermQuery("f", "t") { Boost = 3.0f };

        Assert.NotEqual(a, b);
    }

    // ── SpanOrQuery ──────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Span Or Query: Field Is Empty With No Clauses scenario.
    /// </summary>
    [Fact(DisplayName = "Span Or Query: Field Is Empty With No Clauses")]
    public void SpanOrQuery_Field_IsEmpty_WithNoClauses()
    {
        var q = new SpanOrQuery();
        Assert.Equal(string.Empty, q.Field);
    }

    /// <summary>
    /// Verifies the Span Or Query: Field Returns First Clause Field scenario.
    /// </summary>
    [Fact(DisplayName = "Span Or Query: Field Returns First Clause Field")]
    public void SpanOrQuery_Field_ReturnsFirstClauseField()
    {
        var q = new SpanOrQuery(
            new SpanTermQuery("content", "foo"),
            new SpanTermQuery("content", "bar"));
        Assert.Equal("content", q.Field);
    }

    /// <summary>
    /// Verifies the Span Or Query: Constructor Throws On Null Clauses scenario.
    /// </summary>
    [Fact(DisplayName = "Span Or Query: Constructor Throws On Null Clauses")]
    public void SpanOrQuery_Constructor_ThrowsOnNullClauses()
    {
        Assert.Throws<ArgumentNullException>(() => new SpanOrQuery(null!));
    }

    /// <summary>
    /// Verifies the Span Or Query: Equals Same Clauses scenario.
    /// </summary>
    [Fact(DisplayName = "Span Or Query: Equals Same Clauses")]
    public void SpanOrQuery_Equals_SameClauses()
    {
        var a = new SpanOrQuery(new SpanTermQuery("f", "x"), new SpanTermQuery("f", "y"));
        var b = new SpanOrQuery(new SpanTermQuery("f", "x"), new SpanTermQuery("f", "y"));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>
    /// Verifies the Span Or Query: Not Equals Different Clauses scenario.
    /// </summary>
    [Fact(DisplayName = "Span Or Query: Not Equals Different Clauses")]
    public void SpanOrQuery_NotEquals_DifferentClauses()
    {
        var a = new SpanOrQuery(new SpanTermQuery("f", "x"));
        var b = new SpanOrQuery(new SpanTermQuery("f", "z"));

        Assert.NotEqual(a, b);
    }

    // ── SpanNearQuery ────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Span Near Query: Properties Set Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Span Near Query: Properties Set Correctly")]
    public void SpanNearQuery_Properties_SetCorrectly()
    {
        var clauses = new SpanQuery[] { new SpanTermQuery("f", "a"), new SpanTermQuery("f", "b") };
        var q = new SpanNearQuery(clauses, slop: 2, inOrder: false);

        Assert.Equal(2, q.Slop);
        Assert.False(q.InOrder);
        Assert.Equal(2, q.Clauses.Count);
    }

    /// <summary>
    /// Verifies the Span Near Query: Field Is Empty When No Clauses scenario.
    /// </summary>
    [Fact(DisplayName = "Span Near Query: Field Is Empty When No Clauses")]
    public void SpanNearQuery_Field_IsEmpty_WhenNoClauses()
    {
        var q = new SpanNearQuery([], slop: 0);
        Assert.Equal(string.Empty, q.Field);
    }

    /// <summary>
    /// Verifies the Span Near Query: Constructor Throws On Null Clauses scenario.
    /// </summary>
    [Fact(DisplayName = "Span Near Query: Constructor Throws On Null Clauses")]
    public void SpanNearQuery_Constructor_ThrowsOnNullClauses()
    {
        Assert.Throws<ArgumentNullException>(() => new SpanNearQuery(null!, 1));
    }

    /// <summary>
    /// Verifies the Span Near Query: Constructor Throws On Negative Slop scenario.
    /// </summary>
    [Fact(DisplayName = "Span Near Query: Constructor Throws On Negative Slop")]
    public void SpanNearQuery_Constructor_ThrowsOnNegativeSlop()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpanNearQuery([], slop: -1));
    }

    /// <summary>
    /// Verifies the Span Near Query: Equals Same Clauses Slop And Order scenario.
    /// </summary>
    [Fact(DisplayName = "Span Near Query: Equals Same Clauses Slop And Order")]
    public void SpanNearQuery_Equals_SameClausesSlopAndOrder()
    {
        SpanQuery[] clauses = [new SpanTermQuery("f", "a"), new SpanTermQuery("f", "b")];
        var a = new SpanNearQuery(clauses, 1, true);
        var b = new SpanNearQuery(clauses, 1, true);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>
    /// Verifies the Span Near Query: Not Equals Different Slop scenario.
    /// </summary>
    [Fact(DisplayName = "Span Near Query: Not Equals Different Slop")]
    public void SpanNearQuery_NotEquals_DifferentSlop()
    {
        SpanQuery[] clauses = [new SpanTermQuery("f", "a")];
        var a = new SpanNearQuery(clauses, 1);
        var b = new SpanNearQuery(clauses, 3);

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// Verifies the Span Near Query: Not Equals Different In Order scenario.
    /// </summary>
    [Fact(DisplayName = "Span Near Query: Not Equals Different In Order")]
    public void SpanNearQuery_NotEquals_DifferentInOrder()
    {
        SpanQuery[] clauses = [new SpanTermQuery("f", "a")];
        var a = new SpanNearQuery(clauses, 1, true);
        var b = new SpanNearQuery(clauses, 1, false);

        Assert.NotEqual(a, b);
    }

    // ── SpanNotQuery ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Span Not Query: Field Delegates To Include scenario.
    /// </summary>
    [Fact(DisplayName = "Span Not Query: Field Delegates To Include")]
    public void SpanNotQuery_Field_DelegatesToInclude()
    {
        var q = new SpanNotQuery(
            new SpanTermQuery("body", "hello"),
            new SpanTermQuery("body", "world"));
        Assert.Equal("body", q.Field);
    }

    /// <summary>
    /// Verifies the Span Not Query: Constructor Throws On Null Include scenario.
    /// </summary>
    [Fact(DisplayName = "Span Not Query: Constructor Throws On Null Include")]
    public void SpanNotQuery_Constructor_ThrowsOnNullInclude()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SpanNotQuery(null!, new SpanTermQuery("f", "x")));
    }

    /// <summary>
    /// Verifies the Span Not Query: Constructor Throws On Null Exclude scenario.
    /// </summary>
    [Fact(DisplayName = "Span Not Query: Constructor Throws On Null Exclude")]
    public void SpanNotQuery_Constructor_ThrowsOnNullExclude()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SpanNotQuery(new SpanTermQuery("f", "x"), null!));
    }

    /// <summary>
    /// Verifies the Span Not Query: Equals Same Include And Exclude scenario.
    /// </summary>
    [Fact(DisplayName = "Span Not Query: Equals Same Include And Exclude")]
    public void SpanNotQuery_Equals_SameIncludeAndExclude()
    {
        var a = new SpanNotQuery(new SpanTermQuery("f", "a"), new SpanTermQuery("f", "b"));
        var b = new SpanNotQuery(new SpanTermQuery("f", "a"), new SpanTermQuery("f", "b"));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>
    /// Verifies the Span Not Query: Not Equals Different Exclude scenario.
    /// </summary>
    [Fact(DisplayName = "Span Not Query: Not Equals Different Exclude")]
    public void SpanNotQuery_NotEquals_DifferentExclude()
    {
        var a = new SpanNotQuery(new SpanTermQuery("f", "a"), new SpanTermQuery("f", "b"));
        var b = new SpanNotQuery(new SpanTermQuery("f", "a"), new SpanTermQuery("f", "c"));

        Assert.NotEqual(a, b);
    }

    // ── QueryParseException ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Query Parse Exception: Constructor With Message Only Sets Message scenario.
    /// </summary>
    [Fact(DisplayName = "Query Parse Exception: Constructor With Message Only Sets Message")]
    public void QueryParseException_ConstructorMessageOnly_SetsMessage()
    {
        var ex = new QueryParseException("unexpected token");
        Assert.Equal("unexpected token", ex.Message);
        Assert.Equal(0, ex.Offset);
    }

    /// <summary>
    /// Verifies the Query Parse Exception: Constructor With Offset Sets Both Properties scenario.
    /// </summary>
    [Fact(DisplayName = "Query Parse Exception: Constructor With Offset Sets Both Properties")]
    public void QueryParseException_ConstructorWithOffset_SetsBothProperties()
    {
        var ex = new QueryParseException("unexpected token", 12);
        Assert.Equal("unexpected token", ex.Message);
        Assert.Equal(12, ex.Offset);
    }

    /// <summary>
    /// Verifies the Query Parse Exception: Is Format Exception scenario.
    /// </summary>
    [Fact(DisplayName = "Query Parse Exception: Is Format Exception")]
    public void QueryParseException_IsFormatException()
    {
        var ex = new QueryParseException("bad input");
        Assert.IsAssignableFrom<FormatException>(ex);
    }

    // ── QueryBuilder static factory methods ──────────────────────────────────

    /// <summary>
    /// Verifies the Query Builder: Term Creates Term Query scenario.
    /// </summary>
    [Fact(DisplayName = "Query Builder: Term Creates Term Query")]
    public void QueryBuilder_Term_CreatesTermQuery()
    {
        var q = QueryBuilder.Term("body", "hello");
        Assert.IsType<TermQuery>(q);
        Assert.Equal("body", q.Field);
        Assert.Equal("hello", q.Term);
    }

    /// <summary>
    /// Verifies the Query Builder: Phrase Creates Phrase Query scenario.
    /// </summary>
    [Fact(DisplayName = "Query Builder: Phrase Creates Phrase Query")]
    public void QueryBuilder_Phrase_CreatesPhraseQuery()
    {
        var q = QueryBuilder.Phrase("body", "hello", "world");
        Assert.IsType<PhraseQuery>(q);
        Assert.Equal("body", q.Field);
    }

    /// <summary>
    /// Verifies the Query Builder: Phrase With Slop Sets Slop scenario.
    /// </summary>
    [Fact(DisplayName = "Query Builder: Phrase With Slop Sets Slop")]
    public void QueryBuilder_PhraseWithSlop_SetsSlop()
    {
        var q = QueryBuilder.Phrase("body", 3, "hello", "world");
        Assert.Equal(3, q.Slop);
    }

    /// <summary>
    /// Verifies the Query Builder: Prefix Creates Prefix Query scenario.
    /// </summary>
    [Fact(DisplayName = "Query Builder: Prefix Creates Prefix Query")]
    public void QueryBuilder_Prefix_CreatesPrefixQuery()
    {
        var q = QueryBuilder.Prefix("body", "hel");
        Assert.IsType<PrefixQuery>(q);
        Assert.Equal("hel", q.Prefix);
    }

    /// <summary>
    /// Verifies the Query Builder: Fuzzy Creates Fuzzy Query With Default Max Edits scenario.
    /// </summary>
    [Fact(DisplayName = "Query Builder: Fuzzy Creates Fuzzy Query With Default Max Edits")]
    public void QueryBuilder_Fuzzy_CreatesFuzzyQueryWithDefaultMaxEdits()
    {
        var q = QueryBuilder.Fuzzy("body", "hello");
        Assert.IsType<FuzzyQuery>(q);
        Assert.Equal(2, q.MaxEdits);
    }

    /// <summary>
    /// Verifies the Query Builder: Wildcard Creates Wildcard Query scenario.
    /// </summary>
    [Fact(DisplayName = "Query Builder: Wildcard Creates Wildcard Query")]
    public void QueryBuilder_Wildcard_CreatesWildcardQuery()
    {
        var q = QueryBuilder.Wildcard("body", "hel*");
        Assert.IsType<WildcardQuery>(q);
        Assert.Equal("hel*", q.Pattern);
    }

    /// <summary>
    /// Verifies the Query Builder: Range Creates Range Query scenario.
    /// </summary>
    [Fact(DisplayName = "Query Builder: Range Creates Range Query")]
    public void QueryBuilder_Range_CreatesRangeQuery()
    {
        var q = QueryBuilder.Range("price", 10.0, 50.0);
        Assert.IsType<RangeQuery>(q);
        Assert.Equal(10.0, q.Min);
        Assert.Equal(50.0, q.Max);
    }

    /// <summary>
    /// Verifies the Query Builder: Term Range Creates Term Range Query scenario.
    /// </summary>
    [Fact(DisplayName = "Query Builder: Term Range Creates Term Range Query")]
    public void QueryBuilder_TermRange_CreatesTermRangeQuery()
    {
        var q = QueryBuilder.TermRange("category", "apple", "cherry");
        Assert.IsType<TermRangeQuery>(q);
    }

    /// <summary>
    /// Verifies the Query Builder: Regexp Creates Regexp Query scenario.
    /// </summary>
    [Fact(DisplayName = "Query Builder: Regexp Creates Regexp Query")]
    public void QueryBuilder_Regexp_CreatesRegexpQuery()
    {
        var q = QueryBuilder.Regexp("body", "hel+o");
        Assert.IsType<RegexpQuery>(q);
        Assert.Equal("hel+o", q.Pattern);
    }

    /// <summary>
    /// Verifies the Query Builder: Constant Score Creates Constant Score Query scenario.
    /// </summary>
    [Fact(DisplayName = "Query Builder: Constant Score Creates Constant Score Query")]
    public void QueryBuilder_ConstantScore_CreatesConstantScoreQuery()
    {
        var inner = new TermQuery("f", "t");
        var q = QueryBuilder.ConstantScore(inner, 5.0f);
        Assert.IsType<ConstantScoreQuery>(q);
        Assert.Equal(5.0f, q.ConstantScore);
    }

    /// <summary>
    /// Verifies the Query Builder: Vector Creates Vector Query scenario.
    /// </summary>
    [Fact(DisplayName = "Query Builder: Vector Creates Vector Query")]
    public void QueryBuilder_Vector_CreatesVectorQuery()
    {
        var q = QueryBuilder.Vector("embedding", [1.0f, 2.0f, 3.0f], topK: 5);
        Assert.IsType<VectorQuery>(q);
        Assert.Equal(5, q.TopK);
    }

    /// <summary>
    /// Verifies the Query Builder: Dis Max Creates Disjunction Max Query scenario.
    /// </summary>
    [Fact(DisplayName = "Query Builder: Dis Max Creates Disjunction Max Query")]
    public void QueryBuilder_DisMax_CreatesDisjunctionMaxQuery()
    {
        var q = QueryBuilder.DisMax(0.1f,
            new TermQuery("title", "foo"),
            new TermQuery("body", "foo"));

        Assert.IsType<DisjunctionMaxQuery>(q);
        Assert.Equal(0.1f, q.TieBreakerMultiplier);
        Assert.Equal(2, q.Disjuncts.Count);
    }

    /// <summary>
    /// Verifies the Query Builder: Bool Builds Boolean Query scenario.
    /// </summary>
    [Fact(DisplayName = "Query Builder: Bool Builds Boolean Query")]
    public void QueryBuilder_Bool_BuildsBooleanQuery()
    {
        var q = QueryBuilder.Bool(b =>
        {
            b.Must(new TermQuery("f", "x"));
            b.Should(new TermQuery("f", "y"));
        });

        Assert.IsType<BooleanQuery>(q);
        Assert.Equal(2, q.Clauses.Count);
    }
}

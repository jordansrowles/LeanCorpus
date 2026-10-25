using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class ComplexPhraseQueryParserOrderingTests : IClassFixture<TestDirectoryFixture>
{
    private const string OrderedQuery = "\"quick (fast OR swift) brown\"";
    private const string OrderedQueryWithSlop = "\"quick (fast OR swift) brown\"~2";

    private static readonly (string Id, string Body)[] Documents =
    [
        ("ordered", "quick fast brown"),
        ("ordered-alt", "quick swift brown"),
        ("reversed", "brown fast quick"),
        ("reversed-alt", "brown swift quick"),
        ("partial-swap", "fast quick brown"),
        ("missing-slot", "quick brown"),
        ("far-apart", "quick one two fast brown"),
    ];

    private readonly TestDirectoryFixture _fixture;

    public ComplexPhraseQueryParserOrderingTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void Parse_DefaultInOrderBuildsTheExpectedThreeSlotQuery()
    {
        var parser = new ComplexPhraseQueryParser("body", new StandardAnalyser());

        Assert.True(parser.InOrder);
        SpanNearQuery phrase = Assert.IsType<SpanNearQuery>(parser.Parse(OrderedQuery));

        AssertThreeSlotStructure(phrase, inOrder: true, slop: 0);
    }

    [Fact]
    public void Parse_UnorderedBuildsTheSameSlotsAndSlop()
    {
        var orderedParser = new ComplexPhraseQueryParser("body", new StandardAnalyser());
        SpanNearQuery ordered = Assert.IsType<SpanNearQuery>(orderedParser.Parse(OrderedQuery));
        var parser = new ComplexPhraseQueryParser("body", new StandardAnalyser())
        {
            InOrder = false,
        };

        SpanNearQuery phrase = Assert.IsType<SpanNearQuery>(parser.Parse(OrderedQuery));

        AssertThreeSlotStructure(phrase, inOrder: false, slop: 0);
        Assert.Equal(ordered.Field, phrase.Field);
        Assert.Equal(ordered.Slop, phrase.Slop);
        Assert.Equal(ordered.Boost, phrase.Boost);
        Assert.Equal(ordered.Clauses, phrase.Clauses);
    }

    [Fact]
    public void Parse_InOrderIsCapturedForEachQueryAndDoesNotMutateExistingResults()
    {
        var parser = new ComplexPhraseQueryParser("body", new StandardAnalyser());

        parser.InOrder = true;
        SpanNearQuery ordered = Assert.IsType<SpanNearQuery>(parser.Parse(OrderedQuery));
        parser.InOrder = false;
        SpanNearQuery unordered = Assert.IsType<SpanNearQuery>(parser.Parse(OrderedQuery));
        parser.InOrder = true;
        SpanNearQuery orderedAgain = Assert.IsType<SpanNearQuery>(parser.Parse(OrderedQuery));

        Assert.True(ordered.InOrder);
        Assert.False(unordered.InOrder);
        Assert.True(orderedAgain.InOrder);

        parser.InOrder = false;
        Assert.True(ordered.InOrder);
        Assert.False(unordered.InOrder);
        Assert.True(orderedAgain.InOrder);
    }

    [Fact]
    public void Parse_InOrderDoesNotChangeOrdinaryAnalysedPhrases()
    {
        var parser = new ComplexPhraseQueryParser("body", new StandardAnalyser())
        {
            InOrder = false,
        };

        Query query = parser.Parse("\"quick brown\"");

        Assert.IsType<PhraseQuery>(query);
    }

    [Fact]
    public void Parse_SingleAlternativeSlotRemainsSpanOrForEitherOrderingValue()
    {
        using IndexSearcher searcher = CreateSearcher(nameof(Parse_SingleAlternativeSlotRemainsSpanOrForEitherOrderingValue));
        var parser = new ComplexPhraseQueryParser("body", new StandardAnalyser())
        {
            InOrder = true,
        };

        SpanOrQuery ordered = Assert.IsType<SpanOrQuery>(parser.Parse("\"(fast OR swift)\""));
        parser.InOrder = false;
        SpanOrQuery unordered = Assert.IsType<SpanOrQuery>(parser.Parse("\"(fast OR swift)\""));

        Assert.Equal(new[] { "fast", "swift" }, GetTerms(ordered));
        Assert.Equal(new[] { "fast", "swift" }, GetTerms(unordered));
        Assert.Equal(GetIds(searcher, ordered), GetIds(searcher, unordered));
    }

    [Fact]
    public void Search_OrderedAlternativesWithZeroSlopMatchOnlyForwardSlots()
    {
        AssertMatches(inOrder: true, slop: 0, "ordered", "ordered-alt");
    }

    [Fact]
    public void Search_UnorderedAlternativesWithZeroSlopAllowReversedSlots()
    {
        AssertMatches(inOrder: false, slop: 0, "ordered", "ordered-alt", "reversed", "reversed-alt");
    }

    [Fact]
    public void Search_OrderedAlternativesWithSlopTwoAllowGapsButNotReversal()
    {
        AssertMatches(inOrder: true, slop: 2, "ordered", "ordered-alt", "far-apart");
    }

    [Fact]
    public void Search_UnorderedAlternativesWithSlopTwoAllowReversedAndPartialSwap()
    {
        AssertMatches(
            inOrder: false,
            slop: 2,
            "ordered",
            "ordered-alt",
            "reversed",
            "reversed-alt",
            "partial-swap",
            "far-apart");
    }

    private void AssertMatches(bool inOrder, int slop, params string[] expectedIds)
    {
        string testName = $"{nameof(ComplexPhraseQueryParserOrderingTests)}_{inOrder}_{slop}";
        using IndexSearcher searcher = CreateSearcher(testName);
        var parser = new ComplexPhraseQueryParser("body", new StandardAnalyser())
        {
            InOrder = inOrder,
        };
        string queryText = slop == 0 ? OrderedQuery : OrderedQueryWithSlop;

        string[] actualIds = GetIds(searcher, parser.Parse(queryText));

        Assert.Equal(expectedIds.OrderBy(static id => id), actualIds);
    }

    private IndexSearcher CreateSearcher(string name)
    {
        string path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);

        var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            foreach ((string id, string body) in Documents)
            {
                var document = new LeanDocument();
                document.Add(new StringField("id", id));
                document.Add(new TextField("body", body));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        return new IndexSearcher(directory);
    }

    private static string[] GetIds(IndexSearcher searcher, Query query) =>
        searcher.Search(query, 10).ScoreDocs
            .Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0])
            .OrderBy(static id => id)
            .ToArray();

    private static string[] GetTerms(SpanOrQuery query) =>
        query.Clauses.Select(static clause => Assert.IsType<SpanTermQuery>(clause).Term).ToArray();

    private static void AssertThreeSlotStructure(SpanNearQuery phrase, bool inOrder, int slop)
    {
        Assert.Equal("body", phrase.Field);
        Assert.Equal(slop, phrase.Slop);
        Assert.Equal(inOrder, phrase.InOrder);
        Assert.Equal(3, phrase.Clauses.Count);
        AssertTerm("quick", phrase.Clauses[0]);
        Assert.Equal(new[] { "fast", "swift" }, GetTerms(Assert.IsType<SpanOrQuery>(phrase.Clauses[1])));
        AssertTerm("brown", phrase.Clauses[2]);
    }

    private static void AssertTerm(string expected, SpanQuery query)
    {
        SpanTermQuery term = Assert.IsType<SpanTermQuery>(query);
        Assert.Equal("body", term.Field);
        Assert.Equal(expected, term.Term);
    }
}

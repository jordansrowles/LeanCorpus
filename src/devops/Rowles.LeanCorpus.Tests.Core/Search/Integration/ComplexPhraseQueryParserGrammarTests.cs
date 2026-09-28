using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class ComplexPhraseQueryParserGrammarTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public ComplexPhraseQueryParserGrammarTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void Parse_FlatAlternativeGroupProducesOneSpanSlotBetweenPhraseTerms()
    {
        var parser = new ComplexPhraseQueryParser("body", new StandardAnalyser());

        var phrase = Assert.IsType<SpanNearQuery>(
            parser.Parse("\"quick (fast OR swift OR rapid) brown\""));

        Assert.True(phrase.InOrder);
        Assert.Equal(3, phrase.Clauses.Count);
        Assert.Equal("quick", Assert.IsType<SpanTermQuery>(phrase.Clauses[0]).Term);
        var alternatives = Assert.IsType<SpanOrQuery>(phrase.Clauses[1]);
        Assert.Equal(
            new[] { "fast", "swift", "rapid" },
            alternatives.Clauses.Select(static clause => Assert.IsType<SpanTermQuery>(clause).Term));
        Assert.Equal("brown", Assert.IsType<SpanTermQuery>(phrase.Clauses[2]).Term);

        using var searcher = CreateSearcher(
            nameof(Parse_FlatAlternativeGroupProducesOneSpanSlotBetweenPhraseTerms),
            ("fast", "quick fast brown"),
            ("swift", "quick swift brown"),
            ("wrong-order", "fast quick brown"),
            ("missing-slot", "quick brown"));
        Assert.Equal(new[] { "fast", "swift" }, GetIds(searcher, phrase));
    }

    [Theory]
    [InlineData("\"(quick fast)\"")]
    [InlineData("\"(OR quick fast)\"")]
    [InlineData("\"(quick OR)\"")]
    [InlineData("\"(quick OR OR fast)\"")]
    [InlineData("\"((quick OR fast) OR swift)\"")]
    [InlineData("\"(quick AND fast)\"")]
    public void Parse_RejectsMalformedOrNestedAlternativeGroups(string queryText)
    {
        var parser = new ComplexPhraseQueryParser("body", new StandardAnalyser());

        Assert.Throws<QueryParseException>(() => parser.Parse(queryText));
    }

    private IndexSearcher CreateSearcher(string name, params (string Id, string Body)[] documents)
    {
        string path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);

        var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            foreach (var document in documents)
            {
                var value = new LeanDocument();
                value.Add(new StringField("id", document.Id));
                value.Add(new TextField("body", document.Body));
                writer.AddDocument(value);
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
}

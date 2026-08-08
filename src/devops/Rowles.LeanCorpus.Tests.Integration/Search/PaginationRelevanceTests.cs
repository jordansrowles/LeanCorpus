using System.Text;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

[Trait("Category", "Search")]
[Trait("Category", "PaginationRelevance")]
public sealed class PaginationRelevanceTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public PaginationRelevanceTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void SearchAfter_ScoreCursorUsesDocIdAsTieBreaker()
    {
        using var searcher = CreateSearcher(
            nameof(SearchAfter_ScoreCursorUsesDocIdAsTieBreaker),
            ("a", "common", 1),
            ("b", "common", 1),
            ("c", "common", 1),
            ("d", "common", 1));
        var query = new TermQuery("body", "common");

        var first = searcher.Search(query, 2);
        var second = searcher.SearchAfter(first.ScoreDocs[^1], query, 2);

        Assert.Equal(new[] { 0, 1 }, first.ScoreDocs.Select(static hit => hit.DocId));
        Assert.Equal(new[] { 2, 3 }, second.ScoreDocs.Select(static hit => hit.DocId));
        Assert.Equal(4, second.TotalHits);
    }

    [Fact]
    public void SearchAfter_MultipleSortFieldsPreserveCursorOrder()
    {
        using var searcher = CreateSearcher(
            nameof(SearchAfter_MultipleSortFieldsPreserveCursorOrder),
            ("a", "common", 3),
            ("b", "common", 2),
            ("c", "other common", 1),
            ("d", "other common", 4));
        var query = new TermQuery("body", "common");
        var sorts = new[]
        {
            SortField.String("group"),
            SortField.Numeric("rank", descending: true)
        };

        var first = searcher.Search(query, 2, sorts);
        var second = searcher.SearchAfter(first.ScoreDocs[^1], query, 2, sorts);
        var ids = second.ScoreDocs
            .Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0])
            .ToArray();

        Assert.Equal(new[] { "d", "c" }, ids);
    }

    [Fact]
    public void QueryRescorer_DoesNotIntroduceSecondPassOnlyDocuments()
    {
        using var searcher = CreateSearcher(
            nameof(QueryRescorer_DoesNotIntroduceSecondPassOnlyDocuments),
            ("candidate", "common", 1),
            ("outside", "preferred", 2));
        var firstPass = searcher.Search(new TermQuery("body", "common"), 1);

        var rescored = new QueryRescorer(
            new TermQuery("body", "preferred"),
            firstPassWeight: 2,
            secondPassWeight: 10)
            .Rescore(searcher, firstPass, 10);

        Assert.Single(rescored.ScoreDocs);
        Assert.Equal(firstPass.ScoreDocs[0].DocId, rescored.ScoreDocs[0].DocId);
        Assert.Equal(firstPass.ScoreDocs[0].Score * 2, rescored.ScoreDocs[0].Score, 5);
    }

    [Fact]
    public void DoubleValuesSource_ComposesNumericFieldsConstantsAndScores()
    {
        using var searcher = CreateSearcher(
            nameof(DoubleValuesSource_ComposesNumericFieldsConstantsAndScores),
            ("low", "common", 1),
            ("high", "common", 5));
        var source = DoubleValuesSource.FromDoubleField("rank")
            .Multiply(DoubleValuesSource.Constant(2))
            .Add(DoubleValuesSource.Scores);

        var results = searcher.Search(new FunctionQuery(source), 2);

        Assert.Equal("high", searcher.GetStoredFields(results.ScoreDocs[0].DocId)["id"][0]);
        Assert.True(results.ScoreDocs[0].Score > results.ScoreDocs[1].Score);
    }

    [Fact]
    public void FunctionScoreQuery_AcceptsComposedValuesSource()
    {
        using var searcher = CreateSearcher(
            nameof(FunctionScoreQuery_AcceptsComposedValuesSource),
            ("low", "common", 1),
            ("high", "common", 5));
        var source = DoubleValuesSource.FromDoubleField("rank")
            .Add(DoubleValuesSource.Constant(1));
        var query = new FunctionScoreQuery(
            new TermQuery("body", "common"),
            source,
            ScoreMode.Multiply);

        var results = searcher.Search(query, 2);

        Assert.Equal("high", searcher.GetStoredFields(results.ScoreDocs[0].DocId)["id"][0]);
    }

    [Fact]
    public void ExtendedSpanQueriesComposePositionAwareMatches()
    {
        using var searcher = CreateSpanSearcher();
        var alpha = new SpanTermQuery("body", "alpha");
        var beta = new SpanTermQuery("body", "beta");
        var gamma = new SpanTermQuery("body", "gamma");
        var enclosing = new SpanNearQuery([alpha, gamma], slop: 1, inOrder: true);

        Assert.Equal(
            new[] { "b" },
            GetIds(searcher, new SpanFirstQuery(beta, end: 1)));
        Assert.Equal(
            new[] { "a" },
            GetIds(searcher, new SpanContainingQuery(enclosing, beta)));
        Assert.Equal(
            new[] { "a" },
            GetIds(searcher, new SpanWithinQuery(beta, enclosing)));
        Assert.Equal(
            new[] { "a", "b", "c" },
            GetIds(searcher, new SpanMultiTermQueryWrapper(new PrefixQuery("body", "alph"))));
    }

    [Fact]
    public void FieldMaskingSpanQuery_AllowsCrossFieldPositionComposition()
    {
        string path = SubDir(nameof(FieldMaskingSpanQuery_AllowsCrossFieldPositionComposition));
        var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", "match"));
            document.Add(new TextField("title", "lead alpha"));
            document.Add(new TextField("body", "beta"));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var query = new SpanNearQuery(
            [
                new SpanTermQuery("body", "beta"),
                new FieldMaskingSpanQuery(new SpanTermQuery("title", "alpha"), "body")
            ],
            slop: 0,
            inOrder: true);

        Assert.Equal(new[] { "match" }, GetIds(searcher, query));
    }

    [Fact]
    public void AnalysingQueryParser_AnalysesWildcardLiteralSections()
    {
        var parser = new AnalysingQueryParser("body", new StandardAnalyser());

        var query = Assert.IsType<PrefixQuery>(parser.Parse("QUICK*"));

        Assert.Equal("quick", query.Prefix);
    }

    [Fact]
    public void AnalysingQueryParser_AnalysesRangeBounds()
    {
        var parser = new AnalysingQueryParser("body", new StandardAnalyser());

        var query = Assert.IsType<TermRangeQuery>(parser.Parse("[ALPHA TO OMEGA]"));

        Assert.Equal("alpha", query.LowerTerm);
        Assert.Equal("omega", query.UpperTerm);
    }

    [Fact]
    public void ComplexPhraseQueryParser_ExpandsAlternativesAndMultiTermClauses()
    {
        var parser = new ComplexPhraseQueryParser("body", new StandardAnalyser());
        using var searcher = CreateSearcher(
            nameof(ComplexPhraseQueryParser_ExpandsAlternativesAndMultiTermClauses),
            ("quick", "quick brown", 1),
            ("fast", "fast broken", 2),
            ("miss", "slow brown", 3));

        var query = Assert.IsType<SpanNearQuery>(
            parser.Parse("\"(quick OR fast) bro*\"~1"));
        var ids = GetIds(searcher, query);

        Assert.IsType<SpanOrQuery>(query.Clauses[0]);
        Assert.IsType<SpanMultiTermQueryWrapper>(query.Clauses[1]);
        Assert.Equal(1, query.Slop);
        Assert.Equal(new[] { "fast", "quick" }, ids);
    }

    [Fact]
    public void TermsQuery_MatchesUtf8TermsWithoutStringConversion()
    {
        using var searcher = CreateSearcher(
            nameof(TermsQuery_MatchesUtf8TermsWithoutStringConversion),
            ("latin", "café", 1),
            ("cjk", "東京", 2),
            ("other", "unused", 3));
        var query = new TermsQuery(
            "body",
            Encoding.UTF8.GetBytes("東京"),
            Encoding.UTF8.GetBytes("café"),
            Encoding.UTF8.GetBytes("東京"));

        var ids = GetIds(searcher, query);

        Assert.Equal(2, query.Terms.Count);
        Assert.Equal(2, searcher.Count(query));
        Assert.Equal(new[] { "cjk", "latin" }, ids);
    }

    private IndexSearcher CreateSearcher(
        string name,
        params (string Id, string Body, double Rank)[] documents)
    {
        string path = SubDir(name);
        var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            foreach (var document in documents)
            {
                var value = new LeanDocument();
                value.Add(new StringField("id", document.Id));
                value.Add(new TextField("body", document.Body));
                value.Add(new StringField(
                    "group",
                    document.Body.StartsWith("other", StringComparison.Ordinal) ? "two" : "one"));
                value.Add(new NumericField("rank", document.Rank));
                writer.AddDocument(value);
            }
            writer.Commit();
        }

        return new IndexSearcher(directory);
    }

    private IndexSearcher CreateSpanSearcher()
    {
        string path = SubDir(nameof(CreateSpanSearcher));
        var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            foreach (var (id, body) in new[]
                     {
                         ("a", "alpha beta gamma"),
                         ("b", "beta alpha"),
                         ("c", "alphabet gamma")
                     })
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

    private static string[] GetIds(IndexSearcher searcher, Query query)
        => searcher.Search(query, 10).ScoreDocs
            .Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0])
            .OrderBy(static id => id)
            .ToArray();

    private string SubDir(string name)
    {
        string path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }
}

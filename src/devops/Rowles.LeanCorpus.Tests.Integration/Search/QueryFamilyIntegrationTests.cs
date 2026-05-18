using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

/// <summary>
/// End-to-end tests for the added query families.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "QueryTypes")]
public sealed class QueryFamilyIntegrationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public QueryFamilyIntegrationTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact(DisplayName = "MatchAllDocsQuery: Returns All Live Documents")]
    public void MatchAllDocsQuery_ReturnsAllLiveDocuments()
    {
        var dir = new MMapDirectory(SubDir(nameof(MatchAllDocsQuery_ReturnsAllLiveDocuments)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 0; i < 3; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", i.ToString()));
                doc.Add(new TextField("body", $"doc {i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new MatchAllDocsQuery(), 10);

        Assert.Equal(3, results.TotalHits);
    }

    [Fact(DisplayName = "MatchNoDocsQuery: Returns No Documents")]
    public void MatchNoDocsQuery_ReturnsNoDocuments()
    {
        var dir = new MMapDirectory(SubDir(nameof(MatchNoDocsQuery_ReturnsNoDocuments)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new MatchNoDocsQuery("test"), 10);

        Assert.Equal(0, results.TotalHits);
    }

    [Fact(DisplayName = "FieldExistsQuery: Matches Stored Only Fields")]
    public void FieldExistsQuery_MatchesStoredOnlyFields()
    {
        var dir = new MMapDirectory(SubDir(nameof(FieldExistsQuery_MatchesStoredOnlyFields)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var storedOnly = new LeanDocument();
            storedOnly.Add(new StringField("id", "stored"));
            storedOnly.Add(new StoredField("note", "present"));
            writer.AddDocument(storedOnly);

            var missing = new LeanDocument();
            missing.Add(new StringField("id", "missing"));
            writer.AddDocument(missing);

            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new FieldExistsQuery("note"), 10);

        Assert.Single(results.ScoreDocs);
        Assert.Equal("stored", searcher.GetStoredFields(results.ScoreDocs[0].DocId)["id"][0]);
    }

    [Fact(DisplayName = "TermInSetQuery: Matches Any Provided Term")]
    public void TermInSetQuery_MatchesAnyProvidedTerm()
    {
        var dir = new MMapDirectory(SubDir(nameof(TermInSetQuery_MatchesAnyProvidedTerm)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            foreach (var (id, body) in new[] { ("a", "red"), ("b", "green"), ("c", "blue") })
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", id));
                doc.Add(new TextField("body", body));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermInSetQuery("body", "blue", "red"), 10);
        var ids = results.ScoreDocs.Select(scoreDoc => searcher.GetStoredFields(scoreDoc.DocId)["id"][0]).OrderBy(static id => id).ToArray();

        Assert.Equal(2, results.TotalHits);
        Assert.Equal(new[] { "a", "c" }, ids);
    }

    [Fact(DisplayName = "PointInSetQuery: Matches Any Provided Point")]
    public void PointInSetQuery_MatchesAnyProvidedPoint()
    {
        var dir = new MMapDirectory(SubDir(nameof(PointInSetQuery_MatchesAnyProvidedPoint)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            foreach (var (id, price) in new[] { ("a", 10.0), ("b", 20.0), ("c", 30.0) })
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", id));
                doc.Add(new NumericField("price", price));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new PointInSetQuery("price", 30.0, 10.0), 10);
        var ids = results.ScoreDocs.Select(scoreDoc => searcher.GetStoredFields(scoreDoc.DocId)["id"][0]).OrderBy(static id => id).ToArray();

        Assert.Equal(2, results.TotalHits);
        Assert.Equal(new[] { "a", "c" }, ids);
    }

    [Fact(DisplayName = "MultiPhraseQuery: Alternative Slot Matches Multiple Documents")]
    public void MultiPhraseQuery_AlternativeSlot_MatchesMultipleDocuments()
    {
        var dir = new MMapDirectory(SubDir(nameof(MultiPhraseQuery_AlternativeSlot_MatchesMultipleDocuments)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            foreach (var (id, body) in new[]
                     {
                         ("quick", "quick brown fox"),
                         ("fast", "fast brown fox"),
                         ("wrong", "quick fox brown")
                     })
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", id));
                doc.Add(new TextField("body", body));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var query = new MultiPhraseQuery("body", new[]
        {
            new[] { "fast", "quick" },
            new[] { "brown" }
        });
        var results = searcher.Search(query, 10);
        var ids = results.ScoreDocs.Select(scoreDoc => searcher.GetStoredFields(scoreDoc.DocId)["id"][0]).OrderBy(static id => id).ToArray();

        Assert.Equal(2, results.TotalHits);
        Assert.Equal(new[] { "fast", "quick" }, ids);
    }

    [Fact(DisplayName = "IntervalsQuery: Ordered And NotContaining Honour Span Semantics")]
    public void IntervalsQuery_OrderedAndNotContaining_HonourSpanSemantics()
    {
        var dir = new MMapDirectory(SubDir(nameof(IntervalsQuery_OrderedAndNotContaining_HonourSpanSemantics)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            foreach (var (id, body) in new[]
                     {
                         ("contains", "alpha beta gamma"),
                         ("clean", "alpha gamma"),
                         ("ordered", "alpha middle beta")
                     })
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", id));
                doc.Add(new TextField("body", body));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var ordered = new IntervalsQuery(
            new IntervalsOrderedSource(
                1,
                new IntervalsTermSource("body", "alpha"),
                new IntervalsTermSource("body", "beta")));

        var orderedResults = searcher.Search(ordered, 10);
        Assert.Equal(2, orderedResults.TotalHits);

        var notContaining = new IntervalsQuery(
            new IntervalsNotContainingSource(
                new IntervalsUnorderedSource(
                    2,
                    new IntervalsTermSource("body", "alpha"),
                    new IntervalsTermSource("body", "gamma")),
                new IntervalsTermSource("body", "beta")));

        var notContainingResults = searcher.Search(notContaining, 10);
        Assert.Single(notContainingResults.ScoreDocs);
        Assert.Equal("clean", searcher.GetStoredFields(notContainingResults.ScoreDocs[0].DocId)["id"][0]);
    }

    [Fact(DisplayName = "CombinedFieldsQuery: Matches Across Fields And Honours Weights")]
    public void CombinedFieldsQuery_MatchesAcrossFields_AndHonoursWeights()
    {
        var dir = new MMapDirectory(SubDir(nameof(CombinedFieldsQuery_MatchesAcrossFields_AndHonoursWeights)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var split = new LeanDocument();
            split.Add(new StringField("id", "split"));
            split.Add(new TextField("title", "alpha"));
            split.Add(new TextField("body", "beta"));
            writer.AddDocument(split);

            var titleOnly = new LeanDocument();
            titleOnly.Add(new StringField("id", "title"));
            titleOnly.Add(new TextField("title", "alpha"));
            writer.AddDocument(titleOnly);

            var bodyOnly = new LeanDocument();
            bodyOnly.Add(new StringField("id", "body"));
            bodyOnly.Add(new TextField("body", "alpha"));
            writer.AddDocument(bodyOnly);

            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var allTerms = new CombinedFieldsQuery(["title", "body"], ["alpha", "beta"], minimumShouldMatch: 2);
        var allTermResults = searcher.Search(allTerms, 10);

        Assert.Single(allTermResults.ScoreDocs);
        Assert.Equal("split", searcher.GetStoredFields(allTermResults.ScoreDocs[0].DocId)["id"][0]);

        var weighted = new CombinedFieldsQuery(
            ["title", "body"],
            ["alpha"],
            fieldWeights: new Dictionary<string, float>(StringComparer.Ordinal)
            {
                ["title"] = 2.0f,
                ["body"] = 1.0f
            });

        var weightedResults = searcher.Search(weighted, 10);
        Assert.Equal(3, weightedResults.TotalHits);
        var weightedDocs = weightedResults.ScoreDocs
            .Select(scoreDoc => new
            {
                Id = searcher.GetStoredFields(scoreDoc.DocId)["id"][0],
                scoreDoc.Score
            })
            .ToDictionary(static item => item.Id, StringComparer.Ordinal);

        Assert.True(weightedDocs["title"].Score > weightedDocs["body"].Score);
    }
}

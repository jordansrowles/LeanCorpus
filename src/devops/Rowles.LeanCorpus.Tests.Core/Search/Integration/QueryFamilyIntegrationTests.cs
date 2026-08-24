using System.Net;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// End-to-end tests for the added query families.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
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
        var results = searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken);

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
        var results = searcher.Search(new MatchNoDocsQuery("test"), 10, TestContext.Current.CancellationToken);

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
        var results = searcher.Search(new FieldExistsQuery("note"), 10, TestContext.Current.CancellationToken);

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
        var results = searcher.Search(new TermInSetQuery("body", "blue", "red"), 10, TestContext.Current.CancellationToken);
        var ids = results.ScoreDocs.Select(scoreDoc => searcher.GetStoredFields(scoreDoc.DocId)["id"][0]).OrderBy(static id => id).ToArray();

        Assert.Equal(2, results.TotalHits);
        Assert.Equal(new[] { "a", "c" }, ids);
    }

    [Fact(DisplayName = "SynonymQuery: Matches Alternatives As One Scoring Unit")]
    public void SynonymQuery_MatchesAlternatives_AsOneScoringUnit()
    {
        var dir = new MMapDirectory(SubDir(nameof(SynonymQuery_MatchesAlternatives_AsOneScoringUnit)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            foreach (var (id, body) in new[]
                     {
                         ("quick", "quick"),
                         ("fast", "fast"),
                         ("both", "quick fast"),
                         ("other", "slow")
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
        var results = searcher.Search(new SynonymQuery("body", "quick", "fast"), 10, TestContext.Current.CancellationToken);
        var ids = results.ScoreDocs
            .Select(scoreDoc => searcher.GetStoredFields(scoreDoc.DocId)["id"][0])
            .ToArray();

        Assert.Equal(3, results.TotalHits);
        Assert.Equal("both", ids[0]);
        Assert.DoesNotContain("other", ids);
    }

    [Fact(DisplayName = "Query Rewrite: Custom Query Executes Through Built In Query")]
    public void QueryRewrite_CustomQuery_ExecutesThroughBuiltInQuery()
    {
        var dir = new MMapDirectory(SubDir(nameof(QueryRewrite_CustomQuery_ExecutesThroughBuiltInQuery)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", "rewritten"));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new RewritingQuery(), 10, TestContext.Current.CancellationToken);

        Assert.Single(results.ScoreDocs);
    }

    [Fact(DisplayName = "Query Weight: Custom Scorer Reranks Approximation Candidates")]
    public void QueryWeight_CustomScorer_ReranksApproximationCandidates()
    {
        var dir = new MMapDirectory(SubDir(nameof(QueryWeight_CustomScorer_ReranksApproximationCandidates)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 0; i < 3; i++)
            {
                var document = new LeanDocument();
                document.Add(new TextField("body", "candidate"));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var query = new DocIdWeightQuery();
        var results = searcher.Search(query, 3, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 2, 1, 0 }, results.ScoreDocs.Select(static scoreDoc => scoreDoc.DocId));
        Assert.Equal(3, searcher.Count(query));
    }

    [Fact(DisplayName = "Per Field Similarity: Uses Field Specific Scoring Model")]
    public void PerFieldSimilarity_UsesFieldSpecificScoringModel()
    {
        var dir = new MMapDirectory(SubDir(nameof(PerFieldSimilarity_UsesFieldSpecificScoringModel)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var titleDocument = new LeanDocument();
            titleDocument.Add(new StringField("id", "title"));
            titleDocument.Add(new TextField("title", "match"));
            writer.AddDocument(titleDocument);

            var bodyDocument = new LeanDocument();
            bodyDocument.Add(new StringField("id", "body"));
            bodyDocument.Add(new TextField("body", "match"));
            writer.AddDocument(bodyDocument);
            writer.Commit();
        }

        var config = new IndexSearcherConfig
        {
            Similarity = new ConstantSimilarity(1),
            PerFieldSimilarities = new Dictionary<string, ISimilarity>
            {
                ["title"] = new ConstantSimilarity(10)
            }
        };
        using var searcher = new IndexSearcher(dir, config);
        var query = new BooleanQuery.Builder()
            .Add(new TermQuery("title", "match"), Occur.Should)
            .Add(new TermQuery("body", "match"), Occur.Should)
            .Build();

        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(
            "title",
            searcher.GetStoredFields(results.ScoreDocs[0].DocId)["id"][0]);
        Assert.True(results.ScoreDocs[0].Score > results.ScoreDocs[1].Score);
    }

    private sealed class RewritingQuery : Query
    {
        public override string Field => "body";
        public override Query Rewrite() => new TermQuery(Field, "rewritten");
        public override bool Equals(object? obj) => obj is RewritingQuery;
        public override int GetHashCode() => typeof(RewritingQuery).GetHashCode();
    }

    private sealed class DocIdWeightQuery : Query
    {
        public override string Field => string.Empty;
        public override Weight CreateWeight(IndexSearcher searcher) => new DocIdWeight();
        public override bool Equals(object? obj) => obj is DocIdWeightQuery;
        public override int GetHashCode() => typeof(DocIdWeightQuery).GetHashCode();
    }

    private sealed class DocIdWeight()
        : Weight(new MatchAllDocsQuery())
    {
        public override Scorer CreateScorer(IndexSearcher searcher) => new DocIdScorer();
    }

    private sealed class DocIdScorer : Scorer
    {
        public override float Score(int docId, float approximationScore)
            => approximationScore + docId;
    }

    private sealed class ConstantSimilarity(float score) : ISimilarity
    {
        public float Score(
            int termFreq,
            int docLength,
            float avgDocLength,
            int totalDocCount,
            int docFreq) => score;

        public (float Factor1, float Factor2) PrecomputeFactors(
            int totalDocCount,
            int docFreq,
            float avgDocLength) => (score, 0);

        public float ScorePrecomputed(
            float factor1,
            float factor2,
            int termFreq,
            int docLength) => factor1;
    }

    [Fact(DisplayName = "BooleanQuery: Minimum Should Match Applies To Term Fast Path")]
    public void BooleanQuery_MinimumShouldMatch_AppliesToTermFastPath()
    {
        var dir = new MMapDirectory(SubDir(nameof(BooleanQuery_MinimumShouldMatch_AppliesToTermFastPath)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            foreach (var (id, body) in new[]
                     {
                         ("one", "alpha"),
                         ("two", "alpha beta"),
                         ("three", "alpha beta gamma")
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
        var query = new BooleanQuery.Builder()
            .Add(new TermQuery("body", "alpha"), Occur.Should)
            .Add(new TermQuery("body", "beta"), Occur.Should)
            .Add(new TermQuery("body", "gamma"), Occur.Should)
            .SetMinimumNumberShouldMatch(2)
            .Build();

        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);
        var ids = results.ScoreDocs
            .Select(scoreDoc => searcher.GetStoredFields(scoreDoc.DocId)["id"][0])
            .OrderBy(static id => id)
            .ToArray();

        Assert.Equal(new[] { "three", "two" }, ids);
    }

    [Fact(DisplayName = "BooleanQuery: Minimum Should Match Applies To Mixed Query Fallback")]
    public void BooleanQuery_MinimumShouldMatch_AppliesToMixedQueryFallback()
    {
        var dir = new MMapDirectory(SubDir(nameof(BooleanQuery_MinimumShouldMatch_AppliesToMixedQueryFallback)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            foreach (var (id, body) in new[]
                     {
                         ("one", "alpha"),
                         ("two", "alpha beta"),
                         ("prefix", "alpha betamax")
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
        var query = new BooleanQuery.Builder()
            .Add(new TermQuery("body", "alpha"), Occur.Should)
            .Add(new PrefixQuery("body", "beta"), Occur.Should)
            .SetMinimumNumberShouldMatch(2)
            .Build();

        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.TotalHits);
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
        var results = searcher.Search(new PointInSetQuery("price", 30.0, 10.0), 10, TestContext.Current.CancellationToken);
        var ids = results.ScoreDocs.Select(scoreDoc => searcher.GetStoredFields(scoreDoc.DocId)["id"][0]).OrderBy(static id => id).ToArray();

        Assert.Equal(2, results.TotalHits);
        Assert.Equal(new[] { "a", "c" }, ids);
    }

    [Fact(DisplayName = "Typed Point Queries: Rewrite To Native Numeric Pipelines")]
    public void TypedPointQueries_RewriteToNativeNumericPipelines()
    {
        var dir = new MMapDirectory(SubDir(nameof(TypedPointQueries_RewriteToNativeNumericPipelines)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            foreach (var (id, number) in new[] { ("one", 1), ("two", 2), ("three", 3) })
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", id));
                doc.Add(new Int64Field("integer", number));
                doc.Add(new NumericField("single", number));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        Assert.Equal(
            1,
            searcher.Search(new Int32RangeQuery("integer", 1, 3, false, false), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(
            2,
            searcher.Search(new Int32PointInSetQuery("integer", 1, 3), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(
            1,
            searcher.Search(new SingleRangeQuery("single", 1, 3, false, false), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(
            2,
            searcher.Search(new SinglePointInSetQuery("single", 1, 3), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact(DisplayName = "Binary Queries: Match Ranges And Point Sets")]
    public void BinaryQueries_MatchRangesAndPointSets()
    {
        var dir = new MMapDirectory(SubDir(nameof(BinaryQueries_MatchRangesAndPointSets)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            foreach (byte value in new byte[] { 1, 2, 3 })
            {
                var doc = new LeanDocument();
                doc.Add(new BinaryField("binary", new byte[] { value }));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        Assert.Equal(
            1,
            searcher.Search(new BinaryRangeQuery("binary", new byte[] { 1 }, new byte[] { 3 }, false, false), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(
            2,
            searcher.Search(new BinaryPointInSetQuery("binary", [1], [3]), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact(DisplayName = "IP Address Queries: Match IPv4 And IPv6 Values")]
    public void InetAddressQueries_MatchIpv4AndIpv6Values()
    {
        var dir = new MMapDirectory(SubDir(nameof(InetAddressQueries_MatchIpv4AndIpv6Values)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            foreach (var address in new[]
                     {
                         IPAddress.Parse("10.0.0.1"),
                         IPAddress.Parse("10.0.0.2"),
                         IPAddress.Parse("2001:db8::1")
                     })
            {
                var doc = new LeanDocument();
                doc.Add(new InetAddressField("address", address));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        Assert.Equal(
            2,
            searcher.Search(new InetAddressRangeQuery(
                    "address",
                    IPAddress.Parse("10.0.0.1"),
                    IPAddress.Parse("10.0.0.2")), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(
            2,
            searcher.Search(new InetAddressPointInSetQuery(
                    "address",
                    IPAddress.Parse("10.0.0.2"),
                    IPAddress.Parse("2001:db8::1")), 10, TestContext.Current.CancellationToken).TotalHits);
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
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);
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

        var orderedResults = searcher.Search(ordered, 10, TestContext.Current.CancellationToken);
        Assert.Equal(2, orderedResults.TotalHits);

        var notContaining = new IntervalsQuery(
            new IntervalsNotContainingSource(
                new IntervalsUnorderedSource(
                    2,
                    new IntervalsTermSource("body", "alpha"),
                    new IntervalsTermSource("body", "gamma")),
                new IntervalsTermSource("body", "beta")));

        var notContainingResults = searcher.Search(notContaining, 10, TestContext.Current.CancellationToken);
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
        var allTermResults = searcher.Search(allTerms, 10, TestContext.Current.CancellationToken);

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

        var weightedResults = searcher.Search(weighted, 10, TestContext.Current.CancellationToken);
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

using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Document.Json;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Aggregations;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

/// <summary>
/// End-to-end tests for the Int64 numeric field pipeline.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "Int64")]
public sealed class Int64EndToEndTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public Int64EndToEndTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name)
    {
        var path = System.IO.Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact(DisplayName = "Int64Field: Indexes, Stores, And Ranges")]
    public void Int64Field_IndexesStoresAndRanges()
    {
        var dir = new MMapDirectory(SubDir(nameof(Int64Field_IndexesStoresAndRanges)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            writer.AddDocument(MakeDoc("a", 1_000_000_000_000L));
            writer.AddDocument(MakeDoc("b", 2_000_000_000_000L));
            writer.AddDocument(MakeDoc("c", 3_000_000_000_000L));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        var rangeResults = searcher.Search(new Int64RangeQuery("value", 1_500_000_000_000L, long.MaxValue), 10);
        Assert.Equal(2, rangeResults.TotalHits);
        var rangeIds = GetIdsSorted(searcher, rangeResults);
        Assert.Equal(new[] { "b", "c" }, rangeIds);

        var pointResults = searcher.Search(new Int64PointInSetQuery("value", 1_000_000_000_000L, 3_000_000_000_000L), 10);
        Assert.Equal(2, pointResults.TotalHits);
        var pointIds = GetIdsSorted(searcher, pointResults);
        Assert.Equal(new[] { "a", "c" }, pointIds);

        var sorted = searcher.Search(new MatchAllDocsQuery(), 10, SortField.Int64("value", descending: true));
        var sortedIds = GetIds(searcher, sorted);
        Assert.Equal(new[] { "c", "b", "a" }, sortedIds);

        var (_, aggs) = searcher.SearchWithAggregations(
            new MatchAllDocsQuery(), 10,
            new AggregationRequest("value_stats", "value"));

        Assert.Single(aggs);
        var stats = aggs[0];
        Assert.Equal(3, stats.Count);
        Assert.Equal(1_000_000_000_000L, stats.Min);
        Assert.Equal(3_000_000_000_000L, stats.Max);
        Assert.Equal(6_000_000_000_000L, stats.Sum);
    }

    [Fact(DisplayName = "JsonDocumentMapper: Large Integer Round Trips Through Index")]
    public void JsonDocumentMapper_LargeIntegerRoundTripsThroughIndex()
    {
        var dir = new MMapDirectory(SubDir(nameof(JsonDocumentMapper_LargeIntegerRoundTripsThroughIndex)));
        const long snowflake = 9_223_372_036_854_775_807L;

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var json = $$"""{"id": "snowflake", "value": {{snowflake}}}""";
            var doc = JsonDocumentMapper.FromJsonString(json);
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new Int64PointInSetQuery("value", snowflake), 10);
        Assert.Single(results.ScoreDocs);
        var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        Assert.Equal("snowflake", stored["id"][0]);
    }

    [Fact(DisplayName = "Int64Field: Stored Value Is Readable")]
    public void Int64Field_StoredValueIsReadable()
    {
        var dir = new MMapDirectory(SubDir(nameof(Int64Field_StoredValueIsReadable)));
        const long value = -9_223_372_036_854_775_808L;

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("id", "min"));
            doc.Add(new Int64Field("value", value));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new MatchAllDocsQuery(), 10);
        Assert.Single(results.ScoreDocs);
        var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        Assert.Equal("min", stored["id"][0]);

        var field = Assert.Single(stored["value"]);
        Assert.Equal(value.ToString(System.Globalization.CultureInfo.InvariantCulture), field);
    }

    private static LeanDocument MakeDoc(string id, long value)
    {
        var doc = new LeanDocument();
        doc.Add(new StringField("id", id));
        doc.Add(new Int64Field("value", value));
        return doc;
    }

    private static string[] GetIds(IndexSearcher searcher, TopDocs results)
        => results.ScoreDocs
            .Select(scoreDoc => searcher.GetStoredFields(scoreDoc.DocId)["id"][0])
            .ToArray();

    private static string[] GetIdsSorted(IndexSearcher searcher, TopDocs results)
        => results.ScoreDocs
            .Select(scoreDoc => searcher.GetStoredFields(scoreDoc.DocId)["id"][0])
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
}

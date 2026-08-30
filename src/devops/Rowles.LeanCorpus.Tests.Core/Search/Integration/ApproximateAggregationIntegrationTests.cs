using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Aggregations;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>End-to-end multi-segment coverage for approximate numeric aggregations.</summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class ApproximateAggregationIntegrationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    public ApproximateAggregationIntegrationTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Approximate aggregations: MultiSegment Values Duplicates And Missing")]
    public void SearchWithAggregations_HandlesMultiSegmentValuesDuplicatesAndMissing()
    {
        string path = Path.Combine(_fixture.Path, nameof(SearchWithAggregations_HandlesMultiSegmentValuesDuplicatesAndMissing));
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        using (var writer = new IndexWriter(new MMapDirectory(path), new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance }))
        {
            Add(writer, 1, 10); Add(writer, 1, 100); Add(writer, 2, 1_000); Add(writer);
            writer.Commit();
        }
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        var (_, results) = searcher.SearchWithAggregations(new TermQuery("body", "common"), 1,
            new AggregationRequest("distinct", "value", AggregationType.Cardinality),
            new AggregationRequest("digest", "value", AggregationType.TDigestPercentiles) { Percentiles = [50, 99] },
            new AggregationRequest("hdr", "latency", AggregationType.HdrPercentiles) { HdrHighestTrackableValue = 10_000, Percentiles = [50, 99] });

        Assert.InRange(((CardinalityAggregationResult)results[0]).EstimatedCardinality, 4.5, 5.5);
        Assert.Equal(6, ((PercentileAggregationResult)results[1]).Count);
        Assert.Equal(6, ((PercentileAggregationResult)results[2]).Count);
    }

    [Fact(DisplayName = "Approximate aggregations: Segment Layout And Deletions")]
    public void SearchWithAggregations_IsStableAcrossSegmentsAndExcludesDeletedDocuments()
    {
        string single = Path.Combine(_fixture.Path, nameof(SearchWithAggregations_IsStableAcrossSegmentsAndExcludesDeletedDocuments), "single");
        string multi = Path.Combine(_fixture.Path, nameof(SearchWithAggregations_IsStableAcrossSegmentsAndExcludesDeletedDocuments), "multi");
        string merged = Path.Combine(_fixture.Path, nameof(SearchWithAggregations_IsStableAcrossSegmentsAndExcludesDeletedDocuments), "merged");
        Build(single, 100, merge: false); Build(multi, 1, merge: false); Build(merged, 1, merge: true);

        var singleResults = Search(single);
        var multiResults = Search(multi);
        var mergedResults = Search(merged);
        var singleCardinality = (CardinalityAggregationResult)singleResults[0];
        var multiCardinality = (CardinalityAggregationResult)multiResults[0];
        Assert.InRange(Math.Abs(singleCardinality.EstimatedCardinality - multiCardinality.EstimatedCardinality), 0, .01);
        Assert.Equal(3, ((PercentileAggregationResult)singleResults[1]).Count);
        Assert.Equal(3, ((PercentileAggregationResult)multiResults[1]).Count);
        Assert.Equal(((PercentileAggregationResult)singleResults[1]).Percentiles, ((PercentileAggregationResult)multiResults[1]).Percentiles);
        Assert.Equal(((PercentileAggregationResult)singleResults[1]).Percentiles, ((PercentileAggregationResult)mergedResults[1]).Percentiles);
        Assert.Equal(((PercentileAggregationResult)singleResults[2]).Percentiles, ((PercentileAggregationResult)multiResults[2]).Percentiles);
        Assert.Equal(((PercentileAggregationResult)singleResults[2]).Percentiles, ((PercentileAggregationResult)mergedResults[2]).Percentiles);

        void Build(string path, int maxBufferedDocs, bool merge)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            Directory.CreateDirectory(path);
            using var writer = new IndexWriter(new MMapDirectory(path), new IndexWriterConfig { MaxBufferedDocs = maxBufferedDocs, MergePolicy = NoMergePolicy.Instance });
            Add(writer, 10); Add(writer, 100); Add(writer, 1_000);
            var deleted = new LeanDocument(); deleted.Add(new TextField("body", "common")); deleted.Add(new TextField("id", "deleted")); deleted.Add(new Int64Field("value", 9_999, stored: false)); deleted.Add(new Int64Field("latency", 9_999, stored: false)); writer.AddDocument(deleted);
            writer.Commit(); writer.DeleteDocuments(new TermQuery("id", "deleted")); writer.Commit();
            if (merge) writer.ForceMerge(1);
        }

        static IReadOnlyList<AggregationResult> Search(string path)
        {
            using var searcher = new IndexSearcher(new MMapDirectory(path));
            return searcher.SearchWithAggregations(new TermQuery("body", "common"), 10,
                new AggregationRequest("distinct", "value", AggregationType.Cardinality),
                new AggregationRequest("hdr", "latency", AggregationType.HdrPercentiles) { HdrHighestTrackableValue = 10_000, Percentiles = [50, 99] },
                new AggregationRequest("digest", "value", AggregationType.TDigestPercentiles) { Percentiles = [50, 99] }).Aggregations;
        }
    }

    private static void Add(IndexWriter writer, params long[] values)
    {
        var document = new LeanDocument(); document.Add(new TextField("body", "common"));
        foreach (long value in values) { document.Add(new Int64Field("value", value, stored: false)); document.Add(new Int64Field("latency", value, stored: false)); }
        writer.AddDocument(document);
    }
}

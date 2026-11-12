using System.Globalization;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Aggregations;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Spatial aggregations decode each document's values once and share them across states. A
/// combined request must return exactly what each aggregation returns on its own, for points and
/// shapes on separate fields, multi-valued and missing values, dateline and polar geometry, many
/// segments, deletes and a merge.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class SpatialSharedDecodingTests : IClassFixture<TestDirectoryFixture>
{
    private const int DocumentCount = 300;

    private readonly TestDirectoryFixture _fixture;

    public SpatialSharedDecodingTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private static ISearchAggregationRequest[] Requests() =>
    [
        new AggregationRequest("rank", "rank"),
        new GeoDistanceAggregationRequest("near", "place", new GeoPoint(0, 180),
            [new GeoDistanceRange(null, 500_000), new GeoDistanceRange(500_000, 5_000_000), new GeoDistanceRange(5_000_000, null)]),
        new GeoCentroidAggregationRequest("place-centre", "place"),
        new GeoBoundsAggregationRequest("place-bounds", "place"),
        new GeoCentroidAggregationRequest("area-centre", "area"),
        new GeoBoundsAggregationRequest("area-bounds", "area"),
        new GeoDistanceAggregationRequest("far", "place", new GeoPoint(89, 0), [new GeoDistanceRange(null, 1_000_000)])
    ];

    [Fact(DisplayName = "Spatial Aggregations: A Combined Request Matches Each Aggregation Alone")]
    public void SpatialAggregations_CombinedRequest_MatchesEachAlone()
    {
        int[] deleted = Enumerable.Range(0, DocumentCount).Where(static i => i % 13 == 6).ToArray();
        string? reference = null;
        foreach (int segments in new[] { 1, 7 })
        {
            using var directory = Build("spatial_shared_" + segments, segments, deleted);
            using (var searcher = new IndexSearcher(directory))
                reference = AssertCombinedMatchesSeparate(searcher, reference, $"{segments} segments");

            if (segments > 1)
            {
                using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
                {
                    writer.ForceMerge(1);
                    writer.Commit();
                }
                using var merged = new IndexSearcher(directory);
                AssertCombinedMatchesSeparate(merged, reference, "merged");
            }
        }
    }

    private static string AssertCombinedMatchesSeparate(IndexSearcher searcher, string? reference, string topology)
    {
        var requests = Requests();
        var (_, combined) = searcher.SearchWithAggregations(
            new TermQuery("kind", "doc"), 0, requests, TestContext.Current.CancellationToken);
        Assert.Equal(requests.Length, combined.Count);
        for (int i = 0; i < requests.Length; i++)
        {
            var (_, alone) = searcher.SearchWithAggregations(
                new TermQuery("kind", "doc"), 0, [requests[i]], TestContext.Current.CancellationToken);
            Assert.Equal(Describe(Assert.Single(alone)), Describe(combined[i]));
        }

        string fingerprint = string.Join("\n", combined.Select(Describe));
        if (reference is not null)
            Assert.True(reference == fingerprint, $"{topology}:\n{fingerprint}\nexpected\n{reference}");
        return fingerprint;
    }

    private static string Describe(ISearchAggregationResult result) => result switch
    {
        AggregationResult numeric => string.Create(CultureInfo.InvariantCulture,
            $"{numeric.Name}:{numeric.Count}:{numeric.Min:R}:{numeric.Max:R}:{numeric.Sum:R}"),
        GeoDistanceAggregationResult distance => distance.Name + ":" + string.Join(",",
            distance.Buckets.Select(static b => b.DocumentCount.ToString(CultureInfo.InvariantCulture))),
        GeoCentroidAggregationResult centroid => string.Create(CultureInfo.InvariantCulture,
            $"{centroid.Name}:{centroid.ContributingDocumentCount}:{centroid.Dimension}:{Round(centroid.Centroid?.Latitude)}:{Round(centroid.Centroid?.Longitude)}"),
        GeoBoundsAggregationResult bounds => string.Create(CultureInfo.InvariantCulture,
            $"{bounds.Name}:{bounds.ContributingDocumentCount}:{bounds.Bounds}"),
        _ => throw new InvalidOperationException(result.GetType().Name)
    };

    // Centroids are sums of floating-point terms whose order depends on segment layout.
    private static string Round(double? value) => value is null ? "none" : Math.Round(value.Value, 9).ToString("R", CultureInfo.InvariantCulture);

    private MMapDirectory Build(string name, int segments, int[] deleted)
    {
        string path = Path.Combine(_fixture.Path, name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = (int)Math.Ceiling(DocumentCount / (double)segments),
            MergePolicy = NoMergePolicy.Instance
        });
        for (int i = 0; i < DocumentCount; i++)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", i.ToString(CultureInfo.InvariantCulture), stored: true));
            document.Add(new StringField("kind", "doc"));
            document.Add(new NumericField("rank", i % 37));

            // Points: none, one or three per document, including the dateline and both poles.
            if (i % 5 != 0)
                document.Add(new GeoPointField("place", ((i * 7) % 170) - 85, ((i * 13) % 360) - 180));
            if (i % 9 == 1)
            {
                document.Add(new GeoPointField("place", 90, 0));
                document.Add(new GeoPointField("place", -10, 180));
            }

            // Shapes: a dateline-crossing rectangle, a polar cap or a small box; some documents have none.
            if (i % 4 == 0)
                document.Add(new LatLonShapeField("area", new GeoRectangle(-1, 179, 1, -179)));
            else if (i % 4 == 1)
                document.Add(new LatLonShapeField("area", new GeoRectangle(80, -180, 90, 180)));
            else if (i % 4 == 2)
                document.Add(new LatLonShapeField("area", new GeoRectangle(i % 30, i % 50, (i % 30) + 2, (i % 50) + 3)));
            writer.AddDocument(document);
        }
        writer.Commit();
        foreach (int id in deleted)
            writer.DeleteDocuments(new TermQuery("id", id.ToString(CultureInfo.InvariantCulture)));
        writer.Commit();
        return directory;
    }
}

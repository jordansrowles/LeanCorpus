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
/// Single-value numeric reads and slice-free geo decoding: geo box and distance results match a
/// brute-force oracle at the dateline and poles and for multi-valued points, and numeric
/// aggregations and geo hits are invariant across segment count.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class NumericAndGeoReadInvarianceTests : IClassFixture<TestDirectoryFixture>
{
    // Points per document; several documents have two points.
    private static readonly (double Lat, double Lon)[][] Points =
    [
        [(0, 179.5)], [(0, -179.5)], [(10, 180)], [(-10, -180)],
        [(89.5, 10)], [(90, 0)], [(-89.5, -20)], [(-90, 0)],
        [(45, 175), (45, -100)], [(-45, 20), (-45, -175)],
        [(51.5, -0.1)], [(48.85, 2.35)], [(40.7, -74.0)], [(35.7, 139.7)],
        [(60, 170), (60, -170)], [(-33.9, 151.2), (85, 100)],
        [(1, 1)], [(-1, -1)], [(20, -160)], [(70, 179.9)]
    ];

    private readonly TestDirectoryFixture _fixture;

    public NumericAndGeoReadInvarianceTests(TestDirectoryFixture fixture) => _fixture = fixture;

    public static TheoryData<double, double, double, double> Boxes => new()
    {
        { -15, 15, 170, -170 },  // crosses the dateline
        { 80, 90, -180, 180 },   // north pole cap
        { -90, -80, -180, 180 }, // south pole cap
        { 30, 70, 160, -165 },   // dateline with multi-valued points
        { -60, 60, -10, 10 },
        { -90, 90, -180, 180 }   // everything
    };

    [Theory(DisplayName = "Geo Box: Matches An Oracle At The Dateline, Poles And For Multi-Valued Points")]
    [MemberData(nameof(Boxes))]
    public void GeoBox_MatchesOracle(double minLat, double maxLat, double minLon, double maxLon)
    {
        var expected = Enumerable.Range(0, Points.Length)
            .Where(i => Points[i].Any(p => p.Lat >= minLat && p.Lat <= maxLat && InLongitude(p.Lon, minLon, maxLon)))
            .Select(Id).Order(StringComparer.Ordinal).ToArray();
        foreach (int segments in new[] { 1, 4, Points.Length })
        {
            using var directory = Build("geo_box_" + segments, segments);
            using var searcher = new IndexSearcher(directory);
            var actual = Ids(searcher, new GeoBoundingBoxQuery("location", minLat, maxLat, minLon, maxLon));
            Assert.True(expected.SequenceEqual(actual),
                $"{segments} segments: expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}]");
        }
    }

    public static TheoryData<double, double, double> Circles => new()
    {
        { 0, 180, 200_000 },     // centred on the dateline
        { 89, 0, 300_000 },      // near the north pole
        { -89, 90, 300_000 },    // near the south pole
        { 45, -177, 500_000 },   // reaches a multi-valued document across the dateline
        { 51.5, -0.1, 400_000 }
    };

    [Theory(DisplayName = "Geo Distance: Matches An Oracle At The Dateline, Poles And For Multi-Valued Points")]
    [MemberData(nameof(Circles))]
    public void GeoDistance_MatchesOracle(double lat, double lon, double radius)
    {
        var expected = Enumerable.Range(0, Points.Length)
            .Where(i => Points[i].Min(p => GeoEncodingUtils.HaversineDistance(lat, lon, p.Lat, p.Lon)) <= radius)
            .Select(Id).Order(StringComparer.Ordinal).ToArray();
        foreach (int segments in new[] { 1, 4, Points.Length })
        {
            using var directory = Build("geo_distance_" + segments, segments);
            using var searcher = new IndexSearcher(directory);
            var actual = Ids(searcher, new GeoDistanceQuery("location", lat, lon, radius));
            Assert.True(expected.SequenceEqual(actual),
                $"{segments} segments: expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}]");
        }
    }

    [Fact(DisplayName = "Numeric Aggregations: Results Are Invariant Across Segment Count")]
    public void NumericAggregations_InvariantAcrossSegmentCount()
    {
        AggregationRequest[] requests =
        [
            new AggregationRequest("price", "price"),
            new AggregationRequest("histogram", "price", AggregationType.Histogram) { HistogramInterval = 5 },
            new AggregationRequest("count", "count")
        ];
        string? reference = null;
        foreach (int segments in new[] { 1, 3, Points.Length })
        {
            using var directory = Build("numeric_segments_" + segments, segments);
            using var searcher = new IndexSearcher(directory);
            var (_, results) = searcher.SearchWithAggregations(new TermQuery("kind", "place"), 10, requests);
            string fingerprint = string.Join("|", results.Select(Describe));
            reference ??= fingerprint;
            Assert.Equal(reference, fingerprint);
        }
    }

    private static string Describe(AggregationResult result)
    {
        string text = string.Create(CultureInfo.InvariantCulture,
            $"{result.Name}:{result.Count}:{result.Min:R}:{result.Max:R}:{result.Sum:R}");
        if (result.Buckets is { } buckets)
            text += ":" + string.Join(",", buckets.Select(static b => string.Create(CultureInfo.InvariantCulture, $"{b.LowerBound:R}..{b.UpperBound:R}={b.Count}")));
        return text;
    }

    private static bool InLongitude(double lon, double minLon, double maxLon)
        => minLon <= maxLon ? lon >= minLon && lon <= maxLon : lon >= minLon || lon <= maxLon;

    private static string[] Ids(IndexSearcher searcher, Query query)
    {
        var results = searcher.Search(query, 100, TestContext.Current.CancellationToken);
        Assert.Equal(results.TotalHits, results.ScoreDocs.Length);
        return results.ScoreDocs.Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0]).Order(StringComparer.Ordinal).ToArray();
    }

    private MMapDirectory Build(string name, int segments)
    {
        string path = Path.Combine(_fixture.Path, name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = Math.Max(1, (int)Math.Ceiling(Points.Length / (double)segments)),
            MergePolicy = NoMergePolicy.Instance
        });
        for (int i = 0; i < Points.Length; i++)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", Id(i), stored: true));
            document.Add(new StringField("kind", "place", stored: false));
            foreach (var (lat, lon) in Points[i])
                document.Add(new GeoPointField("location", lat, lon));
            if (i % 5 != 2)
                document.Add(new NumericField("price", i * 1.25));
            if (i % 4 == 1)
                document.Add(new NumericField("price", i * 0.5)); // a second value
            document.Add(new Int64Field("count", i * 7L));
            writer.AddDocument(document);
        }
        writer.Commit();
        return directory;
    }

    private static string Id(int i) => i.ToString(CultureInfo.InvariantCulture);
}

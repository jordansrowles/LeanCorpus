using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Search.Geo;
using Rowles.LeanLucene.Store;
using Xunit;

namespace Rowles.LeanLucene.Tests.Search.Geo;

/// <summary>
/// Contains unit tests for Geo Query.
/// </summary>
public sealed class GeoQueryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "leanlucene_geo_" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>
    /// Builds an index with well-known cities at known coordinates.
    /// </summary>
    private void IndexCities()
    {
        var dir = new MMapDirectory(_dir);
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        AddCity(writer, "London", 51.5074, -0.1278);
        AddCity(writer, "Paris", 48.8566, 2.3522);
        AddCity(writer, "New York", 40.7128, -74.0060);
        AddCity(writer, "Tokyo", 35.6762, 139.6503);
        AddCity(writer, "Sydney", -33.8688, 151.2093);

        writer.Commit();
    }

    private static void AddCity(IndexWriter writer, string name, double lat, double lon)
    {
        var doc = new LeanDocument();
        doc.Add(new StringField("city", name));
        doc.Add(new GeoPointField("location", lat, lon));
        writer.AddDocument(doc);
    }

    /// <summary>
    /// Verifies the Bounding Box: Returns Points Inside Box scenario.
    /// </summary>
    [Fact(DisplayName = "Bounding Box: Returns Points Inside Box")]
    public void BoundingBox_ReturnsPointsInsideBox()
    {
        IndexCities();
        var dir = new MMapDirectory(_dir);
        using var searcher = new IndexSearcher(dir);

        // Box covering Western Europe (London + Paris)
        var query = new GeoBoundingBoxQuery("location", 47.0, 53.0, -2.0, 4.0);
        var results = searcher.Search(query, 10);

        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Bounding Box: Empty Region Returns No Hits scenario.
    /// </summary>
    [Fact(DisplayName = "Bounding Box: Empty Region Returns No Hits")]
    public void BoundingBox_EmptyRegion_ReturnsNoHits()
    {
        IndexCities();
        var dir = new MMapDirectory(_dir);
        using var searcher = new IndexSearcher(dir);

        // Box over middle of Pacific
        var query = new GeoBoundingBoxQuery("location", 0.0, 1.0, -170.0, -169.0);
        var results = searcher.Search(query, 10);

        Assert.Equal(0, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Geo Distance: Finds Cities Within Radius scenario.
    /// </summary>
    [Fact(DisplayName = "Geo Distance: Finds Cities Within Radius")]
    public void GeoDistance_FindsCitiesWithinRadius()
    {
        IndexCities();
        var dir = new MMapDirectory(_dir);
        using var searcher = new IndexSearcher(dir);

        // 400km radius from London — should include London, possibly Paris (~340km)
        var query = new GeoDistanceQuery("location", 51.5074, -0.1278, 400_000.0);
        var results = searcher.Search(query, 10);

        Assert.True(results.TotalHits >= 1, "Should find at least London");
        Assert.True(results.TotalHits <= 2, "Should find at most London and Paris");
    }

    /// <summary>
    /// Verifies the Geo Distance: Small Radius Finds Only Exact City scenario.
    /// </summary>
    [Fact(DisplayName = "Geo Distance: Small Radius Finds Only Exact City")]
    public void GeoDistance_SmallRadius_FindsOnlyExactCity()
    {
        IndexCities();
        var dir = new MMapDirectory(_dir);
        using var searcher = new IndexSearcher(dir);

        // 10km radius centred exactly on Tokyo
        var query = new GeoDistanceQuery("location", 35.6762, 139.6503, 10_000.0);
        var results = searcher.Search(query, 10);

        Assert.Equal(1, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Geo Distance: Large Radius Finds Multiple Cities scenario.
    /// </summary>
    [Fact(DisplayName = "Geo Distance: Large Radius Finds Multiple Cities")]
    public void GeoDistance_LargeRadius_FindsMultipleCities()
    {
        IndexCities();
        var dir = new MMapDirectory(_dir);
        using var searcher = new IndexSearcher(dir);

        // 10,000km radius from centre of Atlantic — should reach London/Paris/NY
        var query = new GeoDistanceQuery("location", 45.0, -30.0, 10_000_000.0);
        var results = searcher.Search(query, 10);

        Assert.True(results.TotalHits >= 3, $"Expected ≥3 hits, got {results.TotalHits}");
    }

    /// <summary>
    /// Verifies the Geo Point Field: Stored Field Round-trip scenario.
    /// </summary>
    [Fact(DisplayName = "Geo Point Field: Stored Field Round-trip")]
    public void GeoPointField_StoredFieldRoundTrip()
    {
        IndexCities();
        var dir = new MMapDirectory(_dir);
        using var searcher = new IndexSearcher(dir);

        // Find Tokyo via tight bounding box
        var query = new GeoBoundingBoxQuery("location", 35.0, 36.0, 139.0, 140.0);
        var results = searcher.Search(query, 10);
        Assert.Equal(1, results.TotalHits);

        var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        Assert.True(stored.ContainsKey("location"), "Stored field should contain the geo point");
    }

    /// <summary>
    /// Verifies the Geo Encoding Utils: Haversine Distance Known Pair scenario.
    /// </summary>
    [Fact(DisplayName = "Geo Encoding Utils: Haversine Distance Known Pair")]
    public void GeoEncodingUtils_HaversineDistance_KnownPair()
    {
        // London to Paris: ~343km
        double dist = GeoEncodingUtils.HaversineDistance(51.5074, -0.1278, 48.8566, 2.3522);
        Assert.InRange(dist, 340_000, 350_000);
    }

    /// <summary>
    /// Verifies the Geo Encoding Utils: Encode Decode Lat Round Trips scenario.
    /// </summary>
    [Fact(DisplayName = "Geo Encoding Utils: Encode Decode Lat Round Trips")]
    public void GeoEncodingUtils_EncodeDecodeLat_RoundTrips()
    {
        double lat = 51.5074;
        int encoded = GeoEncodingUtils.EncodeLat(lat);
        double decoded = GeoEncodingUtils.DecodeLat(encoded);
        Assert.InRange(decoded, lat - 0.001, lat + 0.001);
    }

    /// <summary>
    /// Verifies the Geo Encoding Utils: Encode Decode Lon Round Trips scenario.
    /// </summary>
    [Fact(DisplayName = "Geo Encoding Utils: Encode Decode Lon Round Trips")]
    public void GeoEncodingUtils_EncodeDecodeLon_RoundTrips()
    {
        double lon = -0.1278;
        int encoded = GeoEncodingUtils.EncodeLon(lon);
        double decoded = GeoEncodingUtils.DecodeLon(encoded);
        Assert.InRange(decoded, lon - 0.001, lon + 0.001);
    }
}

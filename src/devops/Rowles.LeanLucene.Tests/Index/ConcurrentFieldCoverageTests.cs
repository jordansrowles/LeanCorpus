using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Geo;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Verifies that field types beyond text/string/numeric (vectors, geo points,
/// sorted doc values) survive the concurrent indexing path. Previously DWPT
/// silently dropped these.
/// </summary>
public sealed class ConcurrentFieldCoverageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ll-cfc-{Guid.NewGuid():N}");

    public ConcurrentFieldCoverageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>
    /// Verifies the Add Documents Concurrent: Preserves Vectors scenario.
    /// </summary>
    [Fact(DisplayName = "Add Documents Concurrent: Preserves Vectors")]
    public void AddDocumentsConcurrent_PreservesVectors()
    {
        var directory = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 500 }))
        {
            var docs = new List<LeanDocument>();
            for (int i = 0; i < 32; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", i.ToString()));
                var v = new float[] { i / 32f, 1f - i / 32f, 0.5f, 0.25f };
                doc.Add(new VectorField("emb", new ReadOnlyMemory<float>(v)));
                docs.Add(doc);
            }

            writer.AddDocumentsConcurrent(docs);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var query = new VectorQuery("emb", new float[] { 0f, 1f, 0.5f, 0.25f }, topK: 5);
        var results = searcher.Search(query, 5);

        Assert.True(results.TotalHits > 0, "Expected vector hits but the concurrent path dropped vectors.");
    }

    /// <summary>
    /// Verifies the Add Document Lock Free: Preserves Vectors scenario.
    /// </summary>
    [Fact(DisplayName = "Add Document Lock Free: Preserves Vectors")]
    public void AddDocumentLockFree_PreservesVectors()
    {
        var directory = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 100 }))
        {
            writer.InitialiseDwptPool(threadCount: 4);

            for (int i = 0; i < 64; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", i.ToString()));
                var v = new float[] { i / 64f, 1f - i / 64f, 0.5f, 0.25f };
                doc.Add(new VectorField("emb", new ReadOnlyMemory<float>(v)));
                writer.AddDocumentLockFree(doc);
            }

            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var query = new VectorQuery("emb", new float[] { 0f, 1f, 0.5f, 0.25f }, topK: 5);
        var results = searcher.Search(query, 5);

        Assert.True(results.TotalHits > 0);
    }

    /// <summary>
    /// Verifies the Add Documents Concurrent: Preserves Geo Points scenario.
    /// </summary>
    [Fact(DisplayName = "Add Documents Concurrent: Preserves Geo Points")]
    public void AddDocumentsConcurrent_PreservesGeoPoints()
    {
        var directory = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 500 }))
        {
            var docs = new List<LeanDocument>
            {
                MakeCity("London", 51.5074, -0.1278),
                MakeCity("Paris", 48.8566, 2.3522),
                MakeCity("New York", 40.7128, -74.0060),
                MakeCity("Tokyo", 35.6762, 139.6503),
                MakeCity("Sydney", -33.8688, 151.2093),
            };

            writer.AddDocumentsConcurrent(docs);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var query = new GeoBoundingBoxQuery("location", 47.0, 53.0, -2.0, 4.0);
        var results = searcher.Search(query, 10);

        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Add Documents Concurrent: Populates Sorted Doc Values For String Fields scenario.
    /// </summary>
    [Fact(DisplayName = "Add Documents Concurrent: Populates Sorted Doc Values For String Fields")]
    public void AddDocumentsConcurrent_PopulatesSortedDocValuesForStringFields()
    {
        var directory = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 500 }))
        {
            var docs = new List<LeanDocument>();
            for (int i = 0; i < 20; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("category", i % 2 == 0 ? "even" : "odd"));
                doc.Add(new TextField("body", $"row {i}"));
                docs.Add(doc);
            }

            writer.AddDocumentsConcurrent(docs);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var reader = searcher.GetSegmentReaders()[0];

        var values = reader.GetSortedDocValues("category");
        Assert.NotNull(values);
        Assert.Equal(20, values!.Length);
        Assert.All(values, v => Assert.Contains(v, new[] { "even", "odd" }));
    }

    private static LeanDocument MakeCity(string name, double lat, double lon)
    {
        var doc = new LeanDocument();
        doc.Add(new StringField("city", name));
        doc.Add(new GeoPointField("location", lat, lon));
        return doc;
    }
}

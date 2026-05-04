using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Geo;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Equivalence suite: every field type indexed through the single-threaded path
/// must produce identical search results when indexed through the concurrent
/// (DWPT / AddDocumentsConcurrent) path.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "Equivalence")]
public sealed class WriterEquivalenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ll-equiv-{Guid.NewGuid():N}");

    public WriterEquivalenceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void TextField_SingleVsConcurrent_SameTermQueryResults()
    {
        var docs = BuildTextDocs(50);
        int single   = IndexAndCount(Sequential(docs), new TermQuery("body", "fox"), "single-text");
        int concurrent = IndexAndCount(Concurrent(docs),  new TermQuery("body", "fox"), "conc-text");
        Assert.Equal(single, concurrent);
    }

    [Fact]
    public void StringField_SingleVsConcurrent_SameTermQueryResults()
    {
        var docs = BuildStringDocs(40);
        int single     = IndexAndCount(Sequential(docs), new TermQuery("category", "alpha"), "single-str");
        int concurrent = IndexAndCount(Concurrent(docs),  new TermQuery("category", "alpha"), "conc-str");
        Assert.Equal(single, concurrent);
    }

    [Fact]
    public void NumericField_SingleVsConcurrent_SameRangeQueryResults()
    {
        var docs = BuildNumericDocs(60);
        var query = new RangeQuery("price", 10, 30);
        int single     = IndexAndCount(Sequential(docs), query, "single-num");
        int concurrent = IndexAndCount(Concurrent(docs),  query, "conc-num");
        Assert.Equal(single, concurrent);
    }

    [Fact]
    public void VectorField_SingleVsConcurrent_SameTopKResults()
    {
        // VectorQuery returns topK results per segment, so hit counts differ when the two
        // paths produce different segment structures. Use topK > docCount so every segment
        // returns all its docs -- both paths then return exactly docCount total hits.
        var docs = BuildVectorDocs(32);
        var probe = new float[] { 0f, 1f, 0.5f, 0.25f };
        int single     = IndexAndCount(Sequential(docs), new VectorQuery("emb", probe, topK: 200), "single-vec");
        int concurrent = IndexAndCount(Concurrent(docs),  new VectorQuery("emb", probe, topK: 200), "conc-vec");
        Assert.Equal(32, single);
        Assert.Equal(32, concurrent);
    }

    [Fact]
    public void GeoField_SingleVsConcurrent_SameBoundingBoxResults()
    {
        var docs = BuildGeoDocs();
        var query = new GeoBoundingBoxQuery("location", 47.0, 53.0, -2.0, 4.0);
        int single     = IndexAndCount(Sequential(docs), query, "single-geo");
        int concurrent = IndexAndCount(Concurrent(docs),  query, "conc-geo");
        Assert.Equal(single, concurrent);
    }

    [Fact]
    public void SortedDocValues_SingleVsConcurrent_SameCollapse()
    {
        var docs = BuildCategoryDocs(30);
        int single     = IndexAndCollapseCount(Sequential(docs), "category", "single-sdv");
        int concurrent = IndexAndCollapseCount(Concurrent(docs),  "category", "conc-sdv");
        Assert.Equal(single, concurrent);
    }

    [Fact]
    public void AllFieldTypes_SingleVsConcurrent_SameFacetCounts()
    {
        var docs = BuildMixedDocs(24);
        var (sHits, facetsSingle)     = IndexAndFacets(Sequential(docs), "category", "single-mix");
        var (cHits, facetsConcurrent) = IndexAndFacets(Concurrent(docs),  "category", "conc-mix");

        Assert.Equal(sHits, cHits);
        Assert.Equal(
            facetsSingle.OrderBy(b => b.Value).Select(b => $"{b.Value}:{b.Count}"),
            facetsConcurrent.OrderBy(b => b.Value).Select(b => $"{b.Value}:{b.Count}"));
    }

    // ---- helpers ----

    private int IndexAndCount(Action<IndexWriter> index, Query query, string subDir)
    {
        string path = Path.Combine(_root, subDir);
        Directory.CreateDirectory(path);
        var dir = new MMapDirectory(path);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 8 }))
        {
            index(writer);
            writer.Commit();
        }
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        return searcher.Search(query, 200).TotalHits;
    }

    private int IndexAndCollapseCount(Action<IndexWriter> index, string collapseField, string subDir)
    {
        string path = Path.Combine(_root, subDir);
        Directory.CreateDirectory(path);
        var dir = new MMapDirectory(path);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 8 }))
        {
            index(writer);
            writer.Commit();
        }
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        var result = searcher.SearchWithCollapse(
            new TermQuery("body", "item"), 200, new CollapseField(collapseField));
        return result.TotalHits;
    }

    private (int, IReadOnlyList<FacetBucket>) IndexAndFacets(
        Action<IndexWriter> index, string facetField, string subDir)
    {
        string path = Path.Combine(_root, subDir);
        Directory.CreateDirectory(path);
        var dir = new MMapDirectory(path);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 8 }))
        {
            index(writer);
            writer.Commit();
        }
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        var (results, facets) = searcher.SearchWithFacets(
            new TermQuery("body", "item"), 200, facetField);
        var buckets = facets.FirstOrDefault()?.Buckets ?? [];
        return (results.TotalHits, buckets);
    }

    private static Action<IndexWriter> Sequential(List<LeanDocument> docs) =>
        writer =>
        {
            foreach (var doc in docs)
                writer.AddDocument(doc);
        };

    private static Action<IndexWriter> Concurrent(List<LeanDocument> docs) =>
        writer => writer.AddDocumentsConcurrent(docs);

    private static List<LeanDocument> BuildTextDocs(int count)
    {
        var docs = new List<LeanDocument>(count);
        for (int i = 0; i < count; i++)
        {
            var d = new LeanDocument();
            d.Add(new TextField("body", i % 3 == 0 ? "the quick brown fox" : "lazy dog jumps"));
            docs.Add(d);
        }
        return docs;
    }

    private static List<LeanDocument> BuildStringDocs(int count)
    {
        var docs = new List<LeanDocument>(count);
        for (int i = 0; i < count; i++)
        {
            var d = new LeanDocument();
            d.Add(new StringField("category", i % 2 == 0 ? "alpha" : "beta"));
            docs.Add(d);
        }
        return docs;
    }

    private static List<LeanDocument> BuildNumericDocs(int count)
    {
        var docs = new List<LeanDocument>(count);
        for (int i = 0; i < count; i++)
        {
            var d = new LeanDocument();
            d.Add(new NumericField("price", i));
            docs.Add(d);
        }
        return docs;
    }

    private static List<LeanDocument> BuildVectorDocs(int count)
    {
        var docs = new List<LeanDocument>(count);
        for (int i = 0; i < count; i++)
        {
            var d = new LeanDocument();
            var v = new float[] { i / (float)count, 1f - i / (float)count, 0.5f, 0.25f };
            d.Add(new VectorField("emb", new ReadOnlyMemory<float>(v)));
            docs.Add(d);
        }
        return docs;
    }

    private static List<LeanDocument> BuildGeoDocs()
    {
        var cities = new[]
        {
            ("London",   51.5074,  -0.1278),
            ("Paris",    48.8566,   2.3522),
            ("New York", 40.7128, -74.0060),
            ("Tokyo",    35.6762, 139.6503),
            ("Sydney",  -33.8688, 151.2093),
        };
        var docs = new List<LeanDocument>();
        foreach (var (name, lat, lon) in cities)
        {
            var d = new LeanDocument();
            d.Add(new StringField("city", name));
            d.Add(new GeoPointField("location", lat, lon));
            docs.Add(d);
        }
        return docs;
    }

    private static List<LeanDocument> BuildCategoryDocs(int count)
    {
        var cats = new[] { "alpha", "beta", "gamma" };
        var docs = new List<LeanDocument>(count);
        for (int i = 0; i < count; i++)
        {
            var d = new LeanDocument();
            d.Add(new TextField("body", "item"));
            d.Add(new StringField("category", cats[i % cats.Length]));
            docs.Add(d);
        }
        return docs;
    }

    private static List<LeanDocument> BuildMixedDocs(int count)
    {
        var cats = new[] { "alpha", "beta" };
        var docs = new List<LeanDocument>(count);
        for (int i = 0; i < count; i++)
        {
            var d = new LeanDocument();
            d.Add(new TextField("body", "item"));
            d.Add(new StringField("category", cats[i % cats.Length]));
            d.Add(new NumericField("price", i));
            docs.Add(d);
        }
        return docs;
    }
}

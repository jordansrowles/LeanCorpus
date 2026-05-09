using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Codecs.Hnsw;
using Rowles.LeanLucene.Codecs.Fst;
using Rowles.LeanLucene.Codecs.Bkd;
using Rowles.LeanLucene.Codecs.Vectors;
using Rowles.LeanLucene.Codecs.TermVectors;
using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Contains unit tests for HNSW Filtered.
/// </summary>
[Trait("Category", "Phase3")]
public sealed class HnswFilteredTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public HnswFilteredTests(TestDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static float[][] BuildRandomVectors(int count, int dim, int seed)
    {
        var rnd = new Random(seed);
        var vectors = new float[count][];
        for (int i = 0; i < count; i++)
        {
            var v = new float[dim];
            for (int d = 0; d < dim; d++)
                v[d] = (float)(rnd.NextDouble() * 2 - 1);
            vectors[i] = v;
        }
        return vectors;
    }

    private static (MMapDirectory dir, float[][] vectors) BuildIndex(string subDir, int n, int dim, IndexWriterConfig cfg)
    {
        var dir = new MMapDirectory(subDir);
        var vectors = BuildRandomVectors(n, dim, seed: 42);
        using var writer = new IndexWriter(dir, cfg);
        for (int i = 0; i < n; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new VectorField("emb", new ReadOnlyMemory<float>(vectors[i])));
            doc.Add(new TextField("colour", (i % 3) switch { 0 => "red", 1 => "green", _ => "blue" }));
            writer.AddDocument(doc);
        }
        writer.Commit();
        return (dir, vectors);
    }

    /// <summary>
    /// Verifies the Filter: Restricts Results To Matching Docs scenario.
    /// </summary>
    [Fact(DisplayName = "Filter: Restricts Results To Matching Docs")]
    public void Filter_RestrictsResultsToMatchingDocs()
    {
        var cfg = new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            HnswBuildConfig = new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
            HnswSeed = 1L,
        };
        var (dir, _) = BuildIndex(SubDir("hnsw_filter_basic"), n: 90, dim: 16, cfg);

        using var searcher = new IndexSearcher(dir);
        var query = new VectorQuery(
            "emb",
            BuildRandomVectors(1, 16, 100)[0],
            topK: 10,
            efSearch: 64,
            filter: new TermQuery("colour", "red"));

        var results = searcher.Search(query, 10);
        Assert.True(results.TotalHits > 0);
        // All returned docs must match the filter (red, indices % 3 == 0).
        foreach (var sd in results.ScoreDocs)
            Assert.Equal(0, sd.DocId % 3);
    }

    /// <summary>
    /// Verifies the Filter: Highly Selective Brute Force Still Returns Top K scenario.
    /// </summary>
    [Fact(DisplayName = "Filter: Highly Selective Brute Force Still Returns Top K")]
    public void Filter_HighlySelective_BruteForceStillReturnsTopK()
    {
        var cfg = new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            HnswBuildConfig = new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
            HnswSeed = 2L,
        };
        // 60 docs: only 2 will match a unique tag — falls into brute-force selectivity bucket.
        var dir = new MMapDirectory(SubDir("hnsw_filter_selective"));
        var vecs = BuildRandomVectors(60, 16, seed: 7);
        using (var writer = new IndexWriter(dir, cfg))
        {
            for (int i = 0; i < 60; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new VectorField("emb", new ReadOnlyMemory<float>(vecs[i])));
                doc.Add(new TextField("tag", i is 5 or 42 ? "rare" : "common"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var query = new VectorQuery(
            "emb",
            vecs[5], // doc 5 should be the closest match (cos = 1)
            topK: 5,
            filter: new TermQuery("tag", "rare"));

        var results = searcher.Search(query, 5);
        Assert.True(results.TotalHits > 0);
        Assert.True(results.TotalHits <= 2);
        Assert.Equal(5, results.ScoreDocs[0].DocId);
    }

    /// <summary>
    /// Verifies the Filter: No Matches Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Filter: No Matches Returns Empty")]
    public void Filter_NoMatches_ReturnsEmpty()
    {
        var cfg = new IndexWriterConfig { BuildHnswOnFlush = false };
        var (dir, _) = BuildIndex(SubDir("hnsw_filter_empty"), n: 30, dim: 8, cfg);

        using var searcher = new IndexSearcher(dir);
        var query = new VectorQuery(
            "emb",
            BuildRandomVectors(1, 8, 0)[0],
            topK: 5,
            filter: new TermQuery("colour", "magenta"));

        var results = searcher.Search(query, 5);
        Assert.Equal(0, results.TotalHits);
    }
}

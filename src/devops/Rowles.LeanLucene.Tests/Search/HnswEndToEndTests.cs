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
/// Contains unit tests for HNSW End-to-end.
/// </summary>
[Trait("Category", "Phase2")]
public sealed class HnswEndToEndTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public HnswEndToEndTests(TestDirectoryFixture fixture)
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

    /// <summary>
    /// Verifies the HNSW Search: Recall Against Flat Baseline scenario.
    /// </summary>
    [Fact(DisplayName = "HNSW Search: Recall Against Flat Baseline")]
    public void HnswSearch_RecallAgainstFlatBaseline()
    {
        var dir = new MMapDirectory(SubDir("hnsw_e2e_recall"));
        var config = new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            HnswBuildConfig = new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
            HnswSeed = 1337L,
        };

        const int n = 300;
        const int dim = 32;
        var vectors = BuildRandomVectors(n, dim, seed: 11);

        using (var writer = new IndexWriter(dir, config))
        {
            for (int i = 0; i < n; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new VectorField("embedding", new ReadOnlyMemory<float>(vectors[i])));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var query = BuildRandomVectors(1, dim, seed: 99)[0];
        const int topK = 10;

        // Compute exact ground truth via flat cosine.
        var truth = Enumerable.Range(0, n)
            .Select(i => (DocId: i, Score: VectorQuery.CosineSimilarity(query, vectors[i])))
            .OrderByDescending(t => t.Score)
            .Take(topK)
            .Select(t => t.DocId)
            .ToHashSet();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new VectorQuery("embedding", query, topK: topK, efSearch: 100), topK);

        Assert.True(results.TotalHits > 0, "HNSW search returned no hits.");
        var found = results.ScoreDocs.Select(sd => sd.DocId).ToHashSet();
        int overlap = found.Intersect(truth).Count();
        double recall = overlap / (double)topK;
        Assert.True(recall >= 0.8, $"Recall {recall:F2} below 0.80 threshold.");
    }

    /// <summary>
    /// Verifies the Multiple Vector Fields: Per Doc Queried Independently scenario.
    /// </summary>
    [Fact(DisplayName = "Multiple Vector Fields: Per Doc Queried Independently")]
    public void MultipleVectorFields_PerDoc_QueriedIndependently()
    {
        var dir = new MMapDirectory(SubDir("hnsw_multi_field"));
        var cfg = new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            HnswBuildConfig = new HnswBuildConfig { M = 8, M0 = 16, EfConstruction = 50 },
            HnswSeed = 7L,
        };

        // Field A: doc 0 = X axis, doc 1 = Y axis
        // Field B: doc 0 = Y axis, doc 1 = X axis
        using (var writer = new IndexWriter(dir, cfg))
        {
            var d0 = new LeanDocument();
            d0.Add(new VectorField("vecA", new ReadOnlyMemory<float>([1f, 0f, 0f, 0f])));
            d0.Add(new VectorField("vecB", new ReadOnlyMemory<float>([0f, 1f, 0f, 0f])));
            writer.AddDocument(d0);

            var d1 = new LeanDocument();
            d1.Add(new VectorField("vecA", new ReadOnlyMemory<float>([0f, 1f, 0f, 0f])));
            d1.Add(new VectorField("vecB", new ReadOnlyMemory<float>([1f, 0f, 0f, 0f])));
            writer.AddDocument(d1);

            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        var resA = searcher.Search(new VectorQuery("vecA", [1f, 0f, 0f, 0f], topK: 1), 1);
        Assert.Equal(1, resA.TotalHits);
        Assert.Equal(0, resA.ScoreDocs[0].DocId);

        var resB = searcher.Search(new VectorQuery("vecB", [1f, 0f, 0f, 0f], topK: 1), 1);
        Assert.Equal(1, resB.TotalHits);
        Assert.Equal(1, resB.ScoreDocs[0].DocId);
    }

    /// <summary>
    /// Verifies the HNSW Disabled: Falls Back To Flat Scan scenario.
    /// </summary>
    [Fact(DisplayName = "HNSW Disabled: Falls Back To Flat Scan")]
    public void HnswDisabled_FallsBackToFlatScan()
    {
        var dir = new MMapDirectory(SubDir("hnsw_disabled"));
        var cfg = new IndexWriterConfig { BuildHnswOnFlush = false };

        using (var writer = new IndexWriter(dir, cfg))
        {
            // doc 0 = pure X, doc 1 = mostly X, doc 2 = Y, doc 3 = Z, doc 4 = -X
            float[][] vecs =
            [
                [1f, 0f, 0f],
                [0.9f, 0.1f, 0f],
                [0f, 1f, 0f],
                [0f, 0f, 1f],
                [-1f, 0f, 0f],
            ];
            for (int i = 0; i < vecs.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new VectorField("v", new ReadOnlyMemory<float>(vecs[i])));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new VectorQuery("v", [1f, 0f, 0f], topK: 2), 2);
        Assert.True(results.TotalHits > 0);
        // Doc 0 (exact) should be top, doc 1 (close) second.
        Assert.Equal(0, results.ScoreDocs[0].DocId);
    }
}

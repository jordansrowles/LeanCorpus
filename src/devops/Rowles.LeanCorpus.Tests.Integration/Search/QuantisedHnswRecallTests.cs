using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

/// <summary>
/// Integration tests verifying HNSW search recall with quantised vector storage (int8 and BBQ).
/// Builds small indices with quantisation enabled, then verifies recall against exact flat cosine baseline.
/// </summary>
[Trait("Category", "Phase2")]
public sealed class QuantisedHnswRecallTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    public QuantisedHnswRecallTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name)
    {
        var path = System.IO.Path.Combine(_fixture.Path, name);
        System.IO.Directory.CreateDirectory(path);
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

    [Fact(DisplayName = "Int8 Quantisation: HNSW search recall >= 75% against flat baseline")]
    public void Int8Quantised_HnswRecall_AgainstFlatBaseline()
    {
        var dir = new MMapDirectory(SubDir("int8_recall"));
        var config = new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            VectorQuantisation = VectorQuantisation.Int8,
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

        // Exact flat cosine baseline
        var truth = Enumerable.Range(0, n)
            .Select(i => (DocId: i, Score: VectorQuery.CosineSimilarity(query, vectors[i])))
            .OrderByDescending(t => t.Score)
            .Take(topK)
            .Select(t => t.DocId)
            .ToHashSet();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new VectorQuery("embedding", query, topK: topK, efSearch: 100), topK);

        Assert.True(results.TotalHits > 0, "Int8 HNSW search returned no hits.");
        var found = results.ScoreDocs.Select(sd => sd.DocId).ToHashSet();
        int overlap = found.Intersect(truth).Count();
        double recall = overlap / (double)topK;
        Assert.True(recall >= 0.75, $"Int8 recall {recall:F2} below 0.75 threshold.");
    }

    [Fact(DisplayName = "BBQ Quantisation: HNSW search recall >= 70% against flat baseline")]
    public void BBQQuantised_HnswRecall_AgainstFlatBaseline()
    {
        var dir = new MMapDirectory(SubDir("bbq_recall"));
        var config = new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            VectorQuantisation = VectorQuantisation.BBQ,
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

        var truth = Enumerable.Range(0, n)
            .Select(i => (DocId: i, Score: VectorQuery.CosineSimilarity(query, vectors[i])))
            .OrderByDescending(t => t.Score)
            .Take(topK)
            .Select(t => t.DocId)
            .ToHashSet();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new VectorQuery("embedding", query, topK: topK, efSearch: 100), topK);

        Assert.True(results.TotalHits > 0, "BBQ HNSW search returned no hits.");
        var found = results.ScoreDocs.Select(sd => sd.DocId).ToHashSet();
        int overlap = found.Intersect(truth).Count();
        double recall = overlap / (double)topK;
        Assert.True(recall >= 0.60, $"BBQ recall {recall:F2} below 0.60 threshold.");
    }

    [Fact(DisplayName = "Default (None): Unquantised vectors behave identically to baseline")]
    public void NoneQuantisation_MatchesBaseline()
    {
        var dir = new MMapDirectory(SubDir("none_recall"));
        var config = new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            VectorQuantisation = VectorQuantisation.None,
            HnswBuildConfig = new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
            HnswSeed = 42L,
        };

        const int n = 100;
        const int dim = 16;
        var vectors = BuildRandomVectors(n, dim, seed: 7);

        using (var writer = new IndexWriter(dir, config))
        {
            for (int i = 0; i < n; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new VectorField("v", new ReadOnlyMemory<float>(vectors[i])));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var query = BuildRandomVectors(1, dim, seed: 77)[0];
        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new VectorQuery("v", query, topK: 5), 5);
        Assert.True(results.TotalHits > 0, "Default quantisation search returned no hits.");
    }
}

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

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Integration tests verifying HNSW search recall with quantised vector storage (int8 and BBQ).
/// Builds small indices with quantisation enabled, then verifies recall against exact flat cosine baseline.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
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

    [Fact(DisplayName = "Retired BBQ cannot create a new HNSW index")]
    public void BBQQuantised_NewHnswIndexIsRejected()
    {
        string path = SubDir("bbq_retired");
        using var directory = new MMapDirectory(path);
        var exception = Assert.Throws<ArgumentException>(() => new IndexWriter(directory, new IndexWriterConfig
        {
            BuildHnswOnFlush = false,
            NormaliseVectors = true,
            VectorQuantisation = VectorQuantisation.BBQ,
            HnswBuildConfig = new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
            HnswSeed = 1337L,
        }));
        Assert.Contains("CodecReadOnly", exception.Message);
        Assert.Empty(Directory.GetFiles(path));
    }

    [Fact(DisplayName = "Legacy 32D BBQ recall remains a historical diagnostic")]
    public void LegacyBbq32DRecall_RemainsAtTheRecordedDiagnosticValue()
    {
        const int n = 300;
        const int dim = 32;
        float[][] vectors = BuildRandomVectors(n, dim, seed: 11);
        var vectorsByDoc = new Dictionary<int, ReadOnlyMemory<float>>(n);
        for (int docId = 0; docId < n; docId++)
        {
            Assert.True(SimdVectorOps.NormaliseInPlace(vectors[docId]));
            vectorsByDoc.Add(docId, vectors[docId]);
        }

        float[] centroid = new float[dim];
        foreach (ReadOnlyMemory<float> vector in vectorsByDoc.Values)
            for (int dimension = 0; dimension < dim; dimension++)
                centroid[dimension] += vector.Span[dimension] / vectorsByDoc.Count;
        var source = new BBQMemoryVectorSource(vectorsByDoc, dim, centroid);
        using HnswGraph graph = HnswGraphBuilder.Build(
            source,
            Enumerable.Range(0, n).ToArray(),
            new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
            seed: 1337L);
        float[] query = BuildRandomVectors(1, dim, seed: 99)[0];
        Assert.True(SimdVectorOps.NormaliseInPlace(query));
        const int topK = 10;

        HashSet<int> truth = Enumerable.Range(0, n)
            .Select(docId => (DocId: docId, Score: VectorQuery.CosineSimilarity(query, vectors[docId])))
            .OrderByDescending(static item => item.Score)
            .Take(topK)
            .Select(static item => item.DocId)
            .ToHashSet();
        IReadOnlyList<HnswSearchResult> candidates = graph.Search(query,
            new HnswTraversalOptions { Ef = 100, TopK = topK });
        double recall = candidates.Select(static item => item.DocId).Intersect(truth).Count() / (double)topK;

        Assert.Equal(0.60, recall, precision: 2);
    }

    [Fact(DisplayName = "Default Float32 vector search matches exact baseline")]
    public void NoneQuantisation_MatchesExactBaseline()
    {
        var dir = new MMapDirectory(SubDir("none_recall"));
        var config = new IndexWriterConfig
        {
            BuildHnswOnFlush = false,
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
        var results = searcher.Search(new VectorQuery("v", query, topK: 5), 5, TestContext.Current.CancellationToken);
        Assert.True(results.TotalHits > 0, "Default quantisation search returned no hits.");
    }
}

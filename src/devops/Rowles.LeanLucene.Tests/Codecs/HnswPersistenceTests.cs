using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Codecs.Hnsw;
using Rowles.LeanLucene.Codecs.Fst;
using Rowles.LeanLucene.Codecs.Bkd;
using Rowles.LeanLucene.Codecs.Vectors;
using Rowles.LeanLucene.Codecs.TermVectors;
using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Contains unit tests for HNSW Persistence.
/// </summary>
[Trait("Category", "Phase2")]
public sealed class HnswPersistenceTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public HnswPersistenceTests(TestDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class ArrayVectorSource : IVectorSource
    {
        private readonly float[][] _vectors;
        public ArrayVectorSource(float[][] vectors)
        {
            _vectors = vectors;
            Dimension = vectors.Length > 0 ? vectors[0].Length : 0;
        }

        public int Dimension { get; }
        public int Count => _vectors.Length;
        public ReadOnlySpan<float> GetVector(int docId) => _vectors[docId];
        public bool HasVector(int docId) => docId >= 0 && docId < _vectors.Length;
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
    /// Verifies the Roundtrip: Preserves Adjacency scenario.
    /// </summary>
    [Fact(DisplayName = "Roundtrip: Preserves Adjacency")]
    public void Roundtrip_PreservesAdjacency()
    {
        var vectors = BuildRandomVectors(count: 100, dim: 16, seed: 1234);
        var source = new ArrayVectorSource(vectors);
        var config = new HnswBuildConfig { M = 8, M0 = 16, EfConstruction = 50 };

        var built = HnswGraphBuilder.Build(source, Enumerable.Range(0, vectors.Length).ToArray(), config, seed: 42L);
        built.Freeze();

        var path = Path.Combine(_fixture.Path, "hnsw_roundtrip.hnsw");
        HnswWriter.Write(path, built, source.Dimension, normalised: false);

        var loaded = HnswReader.Read(path, source);

        Assert.Equal(built.NodeCount, loaded.NodeCount);
        Assert.Equal(built.MaxLevel, loaded.MaxLevel);
        Assert.Equal(built.EntryPoint, loaded.EntryPoint);
        Assert.Equal(built.Seed, loaded.Seed);
        Assert.Equal(built.M, loaded.M);
        Assert.Equal(built.M0, loaded.M0);
        Assert.Equal(built.EfConstruction, loaded.EfConstruction);

        for (int level = 0; level <= built.MaxLevel; level++)
        {
            var originalNodes = built.GetNodesAtLevel(level).OrderBy(x => x).ToArray();
            var loadedNodes = loaded.GetNodesAtLevel(level).OrderBy(x => x).ToArray();
            Assert.Equal(originalNodes, loadedNodes);

            foreach (var docId in originalNodes)
            {
                var origNeighbours = built.GetNeighbours(docId, level).OrderBy(x => x).ToArray();
                var loadedNeighbours = loaded.GetNeighbours(docId, level).OrderBy(x => x).ToArray();
                Assert.Equal(origNeighbours, loadedNeighbours);
            }
        }
    }

    /// <summary>
    /// Verifies the Roundtrip: Search Results Identical scenario.
    /// </summary>
    [Fact(DisplayName = "Roundtrip: Search Results Identical")]
    public void Roundtrip_SearchResultsIdentical()
    {
        var vectors = BuildRandomVectors(count: 200, dim: 32, seed: 7);
        var source = new ArrayVectorSource(vectors);
        var config = new HnswBuildConfig { M = 8, M0 = 16, EfConstruction = 60 };

        var built = HnswGraphBuilder.Build(source, Enumerable.Range(0, vectors.Length).ToArray(), config, seed: 99L);
        built.Freeze();

        var path = Path.Combine(_fixture.Path, "hnsw_search.hnsw");
        HnswWriter.Write(path, built, source.Dimension, normalised: false);
        var loaded = HnswReader.Read(path, source);

        var query = vectors[0];
        var options = new HnswSearchOptions { Ef = 50, TopK = 10 };

        var origResults = built.Search(query, options).Select(r => r.DocId).ToArray();
        var loadedResults = loaded.Search(query, options).Select(r => r.DocId).ToArray();

        Assert.Equal(origResults, loadedResults);
    }
}

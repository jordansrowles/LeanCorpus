using System.Linq;
using FsCheck.Xunit;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.Search;

[Trait("Category", "Chaos")]
[Trait("Category", "Search")]
public sealed class HnswRecallFuzzTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;
    public HnswRecallFuzzTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Building an HNSW graph from random vectors should not throw and should produce
    /// a non-empty graph with the correct node count.
    /// </summary>
    [Property(MaxTest = 100)]
    public void Build_RandomVectors_ProducesValidGraph(int seed, int dimensions, int nodeCount)
    {
        dimensions = Math.Abs(dimensions % 16) + 2;   // 2..17
        nodeCount = Math.Abs(nodeCount % 200) + 10;    // 10..209

        var vectors = new DenseVectorSource(dimensions, nodeCount);
        int actualSeed = seed == 0 ? 42 : seed;

        var config = new HnswBuildConfig { M = 8, M0 = 16, EfConstruction = 50 };
        var docIds = Enumerable.Range(0, nodeCount).ToList();
        var graph = HnswGraphBuilder.Build(vectors, docIds, config, actualSeed);

        Assert.True(graph.NodeCount == nodeCount,
            $"Expected {nodeCount} nodes, got {graph.NodeCount}");
    }

    /// <summary>
    /// A simple in-memory vector source for testing. Generates normalised random vectors.
    /// </summary>
    private sealed class DenseVectorSource : IVectorSource, IDisposable
    {
        private readonly float[][] _vectors;
        private bool _disposed;

        public DenseVectorSource(int dimensions, int count)
        {
            _vectors = new float[count][];
            var rng = new Random(count * dimensions);
            for (int i = 0; i < count; i++)
            {
                _vectors[i] = new float[dimensions];
                float norm = 0;
                for (int d = 0; d < dimensions; d++)
                {
                    float v = (float)(rng.NextDouble() * 2 - 1);
                    _vectors[i][d] = v;
                    norm += v * v;
                }
                norm = MathF.Sqrt(norm);
                if (norm > 0)
                    for (int d = 0; d < dimensions; d++)
                        _vectors[i][d] /= norm;
            }
        }

        public int Dimension => _vectors.Length > 0 ? _vectors[0].Length : 0;
        public int Count => _vectors.Length;

        public ReadOnlySpan<float> GetVector(int index) => _vectors[index];

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}

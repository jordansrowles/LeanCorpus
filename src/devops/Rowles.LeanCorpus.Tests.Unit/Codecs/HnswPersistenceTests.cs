using System.Buffers;
using System.IO;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Highlighting;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

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

    /// <summary>
    /// Writes a synthetic HNSW file with controlled header values.
    /// Body format: dimension(4) normalised(1) m(4) m0(4) efConstruction(4) seed(8)
    ///              entryPoint(4) maxLevel(4) nodeCount(4) levelCount(4) [levelData...]
    /// </summary>
    private static void WriteSyntheticHnswFile(string filePath,
        int nodeCount, int maxLevel, int levelCount, int dimension = 16,
        Action<BinaryWriter>? writeLevelData = null)
    {
        using var bodyStream = new MemoryStream();
        using (var bw = new BinaryWriter(bodyStream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(dimension);
            bw.Write((byte)0); // normalised
            bw.Write(8);       // m
            bw.Write(16);      // m0
            bw.Write(50);      // efConstruction
            bw.Write(42L);     // seed
            bw.Write(0);       // entryPoint
            bw.Write(maxLevel);
            bw.Write(nodeCount);
            bw.Write(levelCount);
            writeLevelData?.Invoke(bw);
        }
        bodyStream.Position = 0;
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var fileWriter = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false);
        CodecFileHeader.Write(fileWriter, CodecFormats.Hnsw,
            new ReadOnlySpan<byte>(bodyStream.GetBuffer(), 0, (int)bodyStream.Length));
    }

    private sealed class TrivialVectorSource : IVectorSource
    {
        public int Dimension { get; init; }
        public int Count => 0;
        public ReadOnlySpan<float> GetVector(int docId) => throw new NotSupportedException();
        public bool HasVector(int docId) => false;
    }

    [Fact(DisplayName = "Malformed File: Negative nodeCount throws")]
    public void Read_ThrowsOnNegativeNodeCount()
    {
        var path = Path.Combine(_fixture.Path, "malformed_neg_nodecount.hnsw");
        WriteSyntheticHnswFile(path, nodeCount: -1, maxLevel: 0, levelCount: 1);
        var source = new TrivialVectorSource { Dimension = 16 };
        var ex = Assert.Throws<InvalidDataException>(() => HnswReader.Read(path, source));
        Assert.Contains("nodeCount", ex.Message);
    }

    [Fact(DisplayName = "Malformed File: Negative levelCount throws")]
    public void Read_ThrowsOnNegativeLevelCount()
    {
        var path = Path.Combine(_fixture.Path, "malformed_neg_levelcount.hnsw");
        WriteSyntheticHnswFile(path, nodeCount: 10, maxLevel: 0, levelCount: -1);
        var source = new TrivialVectorSource { Dimension = 16 };
        var ex = Assert.Throws<InvalidDataException>(() => HnswReader.Read(path, source));
        Assert.Contains("levelCount", ex.Message);
    }

    [Fact(DisplayName = "Malformed File: levelCount exceeds maxLevel+1 throws")]
    public void Read_ThrowsOnLevelCountExceedsMaxLevel()
    {
        var path = Path.Combine(_fixture.Path, "malformed_levelcount_gt_maxlevel.hnsw");
        WriteSyntheticHnswFile(path, nodeCount: 10, maxLevel: 2, levelCount: 5);
        var source = new TrivialVectorSource { Dimension = 16 };
        var ex = Assert.Throws<InvalidDataException>(() => HnswReader.Read(path, source));
        Assert.Contains("levelCount", ex.Message);
    }

    [Fact(DisplayName = "Malformed File: Negative nodes per level throws")]
    public void Read_ThrowsOnNegativeNodesPerLevel()
    {
        var path = Path.Combine(_fixture.Path, "malformed_neg_nodes.hnsw");
        WriteSyntheticHnswFile(path, nodeCount: 10, maxLevel: 1, levelCount: 1, writeLevelData: bw =>
        {
            bw.Write(-1); // negative nodes count for level 0
        });
        var source = new TrivialVectorSource { Dimension = 16 };
        var ex = Assert.Throws<InvalidDataException>(() => HnswReader.Read(path, source));
        Assert.Contains("nodes", ex.Message);
    }

    [Fact(DisplayName = "Malformed File: Negative neighCount throws")]
    public void Read_ThrowsOnNegativeNeighCount()
    {
        var path = Path.Combine(_fixture.Path, "malformed_neg_neighcount.hnsw");
        WriteSyntheticHnswFile(path, nodeCount: 10, maxLevel: 1, levelCount: 1, writeLevelData: bw =>
        {
            bw.Write(1);  // one node at level 0
            bw.Write(0);  // docId
            bw.Write(-1); // negative neighCount
        });
        var source = new TrivialVectorSource { Dimension = 16 };
        var ex = Assert.Throws<InvalidDataException>(() => HnswReader.Read(path, source));
        Assert.Contains("neighCount", ex.Message);
    }

    [Fact(DisplayName = "Malformed File: neighCount exceeds nodeCount throws")]
    public void Read_ThrowsOnNeighCountExceedsNodeCount()
    {
        var path = Path.Combine(_fixture.Path, "malformed_neighcount_gt_nodecount.hnsw");
        WriteSyntheticHnswFile(path, nodeCount: 5, maxLevel: 1, levelCount: 1, writeLevelData: bw =>
        {
            bw.Write(1); // one node at level 0
            bw.Write(0); // docId
            bw.Write(50); // neighCount > nodeCount (5)
        });
        var source = new TrivialVectorSource { Dimension = 16 };
        var ex = Assert.Throws<InvalidDataException>(() => HnswReader.Read(path, source));
        Assert.Contains("neighCount", ex.Message);
    }
}

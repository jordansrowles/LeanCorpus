using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class BbqOsqHnswTests : IClassFixture<TestDirectoryFixture>
{
    private const int Dimension = 128;
    private readonly TestDirectoryFixture _fixture;

    public BbqOsqHnswTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "BBQ v3 prepares stored graph queries from canonical vectors")]
    public void PrepareStoredDocumentQuery_MatchesDecodedCanonicalQuery()
    {
        var vectors = BuildVectors(count: 4, seed: 19384);
        using QuantisedVectorReader reader = WriteV3(vectors,
            nameof(PrepareStoredDocumentQuery_MatchesDecodedCanonicalQuery));
        var source = new QuantisedVectorSource(reader);
        PreparedBbqQuery fromStoredDocument = source.PrepareBbqQuery(0);
        PreparedBbqQuery fromDecodedCanonical = reader.PrepareBbqQuery(reader.ReadVector(0));

        foreach (int candidateDocId in vectors.Keys)
        {
            Assert.Equal(
                reader.ScoreBbqCandidate(fromDecodedCanonical, candidateDocId),
                source.ScoreBbqCandidate(fromStoredDocument, candidateDocId),
                precision: 6);
        }
    }

    [Fact(DisplayName = "BBQ v3 HNSW returns candidates and scores from the prepared OSQ metric")]
    public void Search_UsesPreparedAsymmetricOsqScoresForEveryCandidate()
    {
        var vectors = BuildVectors(count: 12, seed: 8128);
        using QuantisedVectorReader reader = WriteV3(vectors, nameof(Search_UsesPreparedAsymmetricOsqScoresForEveryCandidate));
        var source = new QuantisedVectorSource(reader);
        using HnswGraph graph = HnswGraphBuilder.Build(
            source,
            vectors.Keys.Order().ToArray(),
            new HnswBuildConfig { M = 4, M0 = 8, EfConstruction = 24 },
            seed: 501);

        float[] query = BuildVector(seed: 991);
        PreparedBbqQuery prepared = reader.PrepareBbqQuery(query);
        var expected = vectors.Keys
            .Select(docId => new HnswSearchResult(docId, reader.ScoreBbqCandidate(prepared, docId)))
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.DocId)
            .ToArray();

        IReadOnlyList<HnswSearchResult> actual = graph.Search(query, new HnswTraversalOptions
        {
            Ef = vectors.Count,
            TopK = vectors.Count,
        });

        Assert.Equal(expected.Select(static candidate => candidate.DocId),
            actual.Select(static candidate => candidate.DocId));
        for (int index = 0; index < expected.Length; index++)
            Assert.Equal(expected[index].Score, actual[index].Score, precision: 6);
    }

    [Fact(DisplayName = "BBQ v3 HNSW crosses a tied OSQ bridge to a closer candidate")]
    public void Search_TraversesBoundaryTieToCloserV3Candidate()
    {
        var vectors = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = BasisVector(1),
            [1] = BasisVector(1),
            [2] = BasisVector(0),
        };
        using QuantisedVectorReader reader = WriteV3(vectors,
            nameof(Search_TraversesBoundaryTieToCloserV3Candidate));
        var source = new QuantisedVectorSource(reader);
        float[] query = BasisVector(0);
        PreparedBbqQuery prepared = reader.PrepareBbqQuery(query);
        Assert.True(reader.ScoreBbqCandidate(prepared, 2) > reader.ScoreBbqCandidate(prepared, 0));

        var level = new HnswGraph.FrozenLevel([0, 1, 2], [[1], [0, 2], [1]]);
        using HnswGraph graph = HnswGraph.FromFrozen(
            source,
            new HnswBuildConfig { M = 2, EfConstruction = 4 },
            seed: 17,
            [level],
            entryPoint: 0,
            maxLevel: 0,
            nodeCount: 3);

        IReadOnlyList<HnswSearchResult> results = graph.Search(query,
            new HnswTraversalOptions { Ef = 1, TopK = 1 });

        Assert.Equal(2, Assert.Single(results).DocId);
    }

    [Fact(DisplayName = "BBQ v3 HNSW topology is stable for the same explicit seed")]
    public void Build_SameSeedProducesIdenticalTopology()
    {
        var vectors = BuildVectors(count: 24, seed: 128384);
        using QuantisedVectorReader reader = WriteV3(vectors, nameof(Build_SameSeedProducesIdenticalTopology));
        var source = new QuantisedVectorSource(reader);
        int[] docIds = vectors.Keys.Order().ToArray();
        var config = new HnswBuildConfig { M = 4, M0 = 8, EfConstruction = 24 };
        using HnswGraph first = HnswGraphBuilder.Build(source, docIds, config, seed: 77123);
        using HnswGraph second = HnswGraphBuilder.Build(source, docIds, config, seed: 77123);

        Assert.Equal(first.LevelCount, second.LevelCount);
        Assert.Equal(first.EntryPoint, second.EntryPoint);
        Assert.Equal(first.MaxLevel, second.MaxLevel);
        for (int level = 0; level < first.LevelCount; level++)
        {
            int[] firstNodes = first.GetNodesAtLevel(level).Order().ToArray();
            int[] secondNodes = second.GetNodesAtLevel(level).Order().ToArray();
            Assert.Equal(firstNodes, secondNodes);
            foreach (int docId in firstNodes)
                Assert.Equal(first.GetNeighbours(docId, level).ToArray(),
                    second.GetNeighbours(docId, level).ToArray());
        }
    }

    private QuantisedVectorReader WriteV3(
        Dictionary<int, ReadOnlyMemory<float>> vectors,
        string name)
    {
        string path = Path.Combine(_fixture.Path, $"{name}_{Guid.NewGuid():N}.vq");
        var source = new InMemoryVectorSource(vectors, Dimension);
        HistoricalBbqVectorFixtures.CopyOsq(path, vectors.Count, Dimension);
        return QuantisedVectorReader.Open(path);
    }

    private static Dictionary<int, ReadOnlyMemory<float>> BuildVectors(int count, int seed)
    {
        var vectors = new Dictionary<int, ReadOnlyMemory<float>>(count);
        for (int docId = 0; docId < count; docId++)
            vectors.Add(docId, BuildVector(seed + docId));
        return vectors;
    }

    private static float[] BuildVector(int seed)
    {
        var random = new Random(seed);
        var vector = new float[Dimension];
        for (int index = 0; index < vector.Length; index++)
            vector[index] = (float)(random.NextDouble() * 2d - 1d);
        Assert.True(VectorMath.NormaliseInPlace(vector));
        return vector;
    }

    private static float[] BasisVector(int index)
    {
        var vector = new float[Dimension];
        vector[index] = 1f;
        return vector;
    }
}

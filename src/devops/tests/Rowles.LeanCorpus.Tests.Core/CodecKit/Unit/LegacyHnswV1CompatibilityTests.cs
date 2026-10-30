using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Search.Simd;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class LegacyHnswV1CompatibilityTests
{
    private const int VectorCount = 192;
    private const int PersistedNodeCount = 96;
    private const int Dimension = 8;
    private const int LegacyEntryPoint = 48;
    private const int LegacyMaximumLevel = 4;
    private const string FixtureResourceName =
        "Rowles.LeanCorpus.Tests.Core.Fixtures.Hnsw.hnsw-v1-legacy-missing-entrypoint.hnsw";
    private const string ValidFixtureResourceName =
        "Rowles.LeanCorpus.Tests.Core.Fixtures.Hnsw.hnsw-v1-legacy-valid-entrypoint.hnsw";

    [Fact(DisplayName = "Legacy HNSW v1 with missing upper entry point remains readable and searchable")]
    public void ReadLegacyGraph_RetainsAndSearchesMissingUpperEntryPoint()
    {
        var vectors = BuildVectors();
        var source = CreateSource(vectors);
        string path = CopyFixture();

        try
        {
            using var graph = HnswReader.Read(path, source, expectedNormalised: true);

            Assert.Equal(PersistedNodeCount, graph.NodeCount);
            Assert.Equal(LegacyEntryPoint, graph.EntryPoint);
            Assert.Equal(LegacyMaximumLevel, graph.MaxLevel);
            Assert.Equal([96, 22, 7, 0, 0], Enumerable.Range(0, graph.LevelCount)
                .Select(level => graph.GetNodesAtLevel(level).Count())
                .ToArray());
            Assert.Contains(graph.EntryPoint, graph.GetNodesAtLevel(0));
            Assert.DoesNotContain(graph.EntryPoint, graph.GetNodesAtLevel(3));
            Assert.DoesNotContain(graph.EntryPoint, graph.GetNodesAtLevel(4));
            Assert.Equal(PersistedNodeCount, CountReachableBaseNodes(graph));
            AssertValidNeighbourReferences(graph);

            var results = graph.Search(BuildQuery(), new HnswTraversalOptions { Ef = 32, TopK = 10 });
            Assert.Equal([26, 60, 41, 24, 33, 88, 44, 56, 50, 19],
                results.Select(static result => result.DocId).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact(DisplayName = "Valid legacy HNSW v1 retains its topology and search results")]
    public void ReadLegacyGraphWithValidEntryPoint_RetainsTopologyAndSearches()
    {
        var vectors = BuildVectors();
        var source = CreateSource(vectors);
        string path = CopyFixture(ValidFixtureResourceName);

        try
        {
            using var graph = HnswReader.Read(path, source, expectedNormalised: true);

            Assert.Equal(PersistedNodeCount, graph.NodeCount);
            Assert.Equal(43, graph.EntryPoint);
            Assert.Equal(2, graph.MaxLevel);
            Assert.Equal([96, 17, 3], Enumerable.Range(0, graph.LevelCount)
                .Select(level => graph.GetNodesAtLevel(level).Count())
                .ToArray());
            Assert.All(Enumerable.Range(0, graph.LevelCount), level =>
                Assert.Contains(graph.EntryPoint, graph.GetNodesAtLevel(level)));
            Assert.Equal(PersistedNodeCount, CountReachableBaseNodes(graph));
            AssertValidNeighbourReferences(graph);

            var results = graph.Search(BuildQuery(), new HnswTraversalOptions { Ef = 32, TopK = 10 });
            Assert.Equal([26, 60, 41, 24, 33, 88, 44, 56, 50, 19],
                results.Select(static result => result.DocId).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact(DisplayName = "Legacy HNSW v1 remap retains topology and round-trips the declared entry point")]
    public void RemapLegacyGraph_RetainsTopologyAndRoundTrips()
    {
        var vectors = BuildVectors();
        var (source, remap) = BuildRemappedSource(vectors, removeLegacyEntryPoint: false);
        string path = CopyFixture();
        string rewrittenPath = NewTemporaryPath();

        try
        {
            using var remapped = HnswReader.Read(path, source, expectedNormalised: true, docIdRemap: remap);

            Assert.Equal(PersistedNodeCount, remapped.NodeCount);
            Assert.Equal(LegacyEntryPoint + 100, remapped.EntryPoint);
            Assert.Equal(2, remapped.MaxLevel);
            Assert.Equal(3, remapped.LevelCount);
            Assert.Contains(remapped.EntryPoint, remapped.GetNodesAtLevel(remapped.MaxLevel));
            AssertValidNeighbourReferences(remapped);

            HnswWriter.Write(rewrittenPath, remapped, Dimension, normalised: true);
            using var reopened = HnswReader.Read(rewrittenPath, source, expectedNormalised: true);
            Assert.Equal(remapped.EntryPoint, reopened.EntryPoint);
            Assert.Equal(remapped.MaxLevel, reopened.MaxLevel);
            AssertValidNeighbourReferences(reopened);
            Assert.Equal(10, reopened.Search(BuildQuery(), new HnswTraversalOptions { Ef = 96, TopK = 10 }).Count);
        }
        finally
        {
            File.Delete(path);
            File.Delete(rewrittenPath);
        }
    }

    [Fact(DisplayName = "Legacy HNSW v1 remap selects a present entry point when the old one is removed")]
    public void RemapLegacyGraph_WhenEntryPointIsRemoved_TrimsEmptyTopLayers()
    {
        var vectors = BuildVectors();
        var (source, remap) = BuildRemappedSource(vectors, removeLegacyEntryPoint: true);
        string path = CopyFixture();

        try
        {
            using var remapped = HnswReader.Read(path, source, expectedNormalised: true, docIdRemap: remap);

            Assert.Equal(PersistedNodeCount - 1, remapped.NodeCount);
            Assert.Equal(2, remapped.MaxLevel);
            Assert.Equal(3, remapped.LevelCount);
            Assert.Contains(remapped.EntryPoint, remapped.GetNodesAtLevel(remapped.MaxLevel));
            AssertValidNeighbourReferences(remapped);
            Assert.Equal(10, remapped.Search(BuildQuery(), new HnswTraversalOptions { Ef = 95, TopK = 10 }).Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact(DisplayName = "Legacy HNSW v1 can be thawed, extended and persisted without invalid neighbour references")]
    public void ThawLegacyGraph_InsertAndPersist_PreservesSearchableTopology()
    {
        var vectors = BuildVectors();
        var source = CreateSource(vectors);
        string path = CopyFixture();
        string rewrittenPath = NewTemporaryPath();

        try
        {
            using var graph = HnswReader.Read(path, source, expectedNormalised: true);
            graph.Thaw();
            for (int docId = PersistedNodeCount; docId < VectorCount; docId++)
                graph.Insert(docId);

            Assert.Equal(VectorCount, graph.NodeCount);
            AssertValidNeighbourReferences(graph);
            Assert.Equal(VectorCount, CountReachableBaseNodes(graph));
            graph.Freeze();
            HnswWriter.Write(rewrittenPath, graph, Dimension, normalised: true);

            using var reopened = HnswReader.Read(rewrittenPath, source, expectedNormalised: true);
            Assert.Equal(VectorCount, reopened.NodeCount);
            AssertValidNeighbourReferences(reopened);
            var results = reopened.Search(BuildQuery(), new HnswTraversalOptions { Ef = VectorCount, TopK = 10 });
            Assert.Equal(10, results.Count);
            Assert.Equal(10, results.Select(static result => result.DocId).Distinct().Count());
            Assert.All(results, result => Assert.InRange(result.DocId, 0, VectorCount - 1));
        }
        finally
        {
            File.Delete(path);
            File.Delete(rewrittenPath);
        }
    }

    private static Dictionary<int, float[]> BuildVectors()
    {
        var random = new Random(1234);
        return Enumerable.Range(0, VectorCount)
            .ToDictionary(static docId => docId, _ => SimdVectorOps.Normalise(RandomVector(random)));
    }

    private static (InMemoryVectorSource Source, Dictionary<int, int> Remap) BuildRemappedSource(
        Dictionary<int, float[]> vectors,
        bool removeLegacyEntryPoint)
    {
        var remap = Enumerable.Range(0, PersistedNodeCount)
            .Where(docId => !removeLegacyEntryPoint || docId != LegacyEntryPoint)
            .ToDictionary(static docId => docId, static docId => docId + 100);
        var remappedVectors = remap.ToDictionary(
            static pair => pair.Value,
            pair => (ReadOnlyMemory<float>)vectors[pair.Key]);
        return (new InMemoryVectorSource(remappedVectors, Dimension), remap);
    }

    private static InMemoryVectorSource CreateSource(Dictionary<int, float[]> vectors)
        => new(vectors.ToDictionary(static pair => pair.Key, static pair => (ReadOnlyMemory<float>)pair.Value), Dimension);

    private static float[] BuildQuery()
        => SimdVectorOps.Normalise(RandomVector(new Random(99)));

    private static float[] RandomVector(Random random)
    {
        var vector = new float[Dimension];
        for (int dimension = 0; dimension < Dimension; dimension++)
            vector[dimension] = (float)(random.NextDouble() * 2d - 1d);
        return vector;
    }

    private static int CountReachableBaseNodes(HnswGraph graph)
    {
        var visited = new HashSet<int> { graph.EntryPoint };
        var pending = new Queue<int>();
        pending.Enqueue(graph.EntryPoint);
        while (pending.TryDequeue(out int docId))
        {
            foreach (int neighbour in graph.GetNeighbours(docId, 0))
                if (visited.Add(neighbour))
                    pending.Enqueue(neighbour);
        }
        return visited.Count;
    }

    private static void AssertValidNeighbourReferences(HnswGraph graph)
    {
        var baseMembers = graph.GetNodesAtLevel(0).ToHashSet();
        Assert.Equal(graph.NodeCount, baseMembers.Count);

        for (int level = 0; level < graph.LevelCount; level++)
        {
            var members = graph.GetNodesAtLevel(level).ToHashSet();
            Assert.Subset(baseMembers, members);
            foreach (int docId in members)
            {
                Assert.Contains(docId, baseMembers);
                foreach (int neighbour in graph.GetNeighbours(docId, level))
                    Assert.Contains(neighbour, members);
            }
        }
    }

    private static string CopyFixture()
        => CopyFixture(FixtureResourceName);

    private static string CopyFixture(string resourceName)
    {
        var stream = typeof(LegacyHnswV1CompatibilityTests).Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded legacy fixture '{resourceName}' was not found.");
        string path = NewTemporaryPath();
        using (stream)
        using (var output = File.Create(path))
            stream.CopyTo(output);
        return path;
    }

    private static string NewTemporaryPath()
        => Path.Combine(Path.GetTempPath(), "leancorpus-hnsw-v1-" + Guid.NewGuid().ToString("N") + ".hnsw");
}

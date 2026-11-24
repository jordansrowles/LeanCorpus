using Rowles.LeanCorpus.Tests.Core.Foundation.Unit;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Per-query allocation of unfiltered vector search must not grow with the number of vectors
/// visited: exact scans borrow stored vectors and reuse one buffer, and graph traversal reuses
/// per-thread scratch (ADR052).
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
[Collection(LeanCorpusDefaultsCollection.Name)]
public sealed class VectorAllocationScalingTests(TestDirectoryFixture fixture) : IClassFixture<TestDirectoryFixture>
{
    private const string Field = "embedding";
    private const int Dimension = 64;

    [Theory(DisplayName = "Vector Allocation: Unfiltered Query Allocation Does Not Grow With Document Count")]
    [InlineData(false)]
    [InlineData(true)]
    public void UnfilteredQueryAllocation_DoesNotGrowWithDocumentCount(bool graph)
    {
        long small = Measure(1_000, graph);
        long large = Measure(8_000, graph);

        // Eight times the vectors. Anything proportional to the vectors visited, even 4 bytes each
        // (28 kB for the extra 7,000), breaks this bound; fixed per-query structures do not.
        Assert.True(large <= small + 16 * 1024, $"1,000 vectors: {small} bytes; 8,000 vectors: {large} bytes.");
    }

    private long Measure(int documentCount, bool graph)
    {
        var random = new Random(9_000 + documentCount); // NOSONAR S2245: deterministic test data
        string path = Path.Combine(fixture.Path, $"allocation-{documentCount}-{graph}");
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            BuildHnswOnFlush = graph,
            MaxBufferedDocs = 100_000,
            MergeThreshold = 100,
        }))
        {
            for (int i = 0; i < documentCount; i++)
            {
                var document = new LeanDocument();
                var vector = new float[Dimension];
                for (int d = 0; d < Dimension; d++)
                    vector[d] = (float)(random.NextDouble() * 2 - 1);
                document.Add(new VectorField(Field, vector));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig { ParallelSearch = false, EnableQueryCache = false });
        var queryVector = new float[Dimension];
        for (int d = 0; d < Dimension; d++)
            queryVector[d] = (float)(random.NextDouble() * 2 - 1);
        var query = new VectorQuery(Field, queryVector, topK: 10);

        for (int warmup = 0; warmup < 40; warmup++)
            Assert.Equal(10, searcher.Search(query, 10).ScoreDocs.Length);

        long minimum = long.MaxValue;
        for (int sample = 0; sample < 5; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var result = searcher.Search(query, 10);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(10, result.ScoreDocs.Length);
            minimum = Math.Min(minimum, allocated);
        }
        return minimum;
    }
}

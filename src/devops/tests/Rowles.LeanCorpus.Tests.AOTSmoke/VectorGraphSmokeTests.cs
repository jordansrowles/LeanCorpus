using Xunit;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.AOTSmoke;

/// <summary>
/// Native AOT smoke test for the opt-in Float32 vector graph (ADR052): flush builds a graph, a
/// merge rebuilds it, and queries take the graph route with exact scores, including when the
/// segment reader state cannot be cached and the graph is reopened for every query.
/// </summary>
public class VectorGraphSmokeTests : IClassFixture<IndexSmokeFixture>
{
    private const int Dimension = 16;
    private const int Count = 1_200;
    private readonly IndexSmokeFixture _fixture;

    public VectorGraphSmokeTests(IndexSmokeFixture fixture) => _fixture = fixture;

    [Fact]
    public void VectorGraph_BuildsMergesAndSearches()
    {
        string path = Path.Combine(_fixture.RootPath, "vector-graph");
        var vectors = new float[Count][];
        for (int i = 0; i < Count; i++)
        {
            vectors[i] = new float[Dimension];
            int cluster = i % 12;
            for (int d = 0; d < Dimension; d++)
                vectors[i][d] = (d == cluster ? 1f : 0f) + (((i * 31 + d * 17) % 97) - 48) / 480f;
        }

        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { BuildHnswOnFlush = true, HnswSeed = 7 }))
        {
            for (int i = 0; i < Count; i++)
            {
                var document = new LeanDocument();
                document.Add(new StringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                document.Add(new VectorField("embedding", vectors[i]));
                writer.AddDocument(document);
                if (i == Count / 2)
                    writer.Commit();
            }
            writer.Commit();
            writer.ForceMerge(1);
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(path);
        foreach (long cacheBytes in new[] { 256L * 1024 * 1024, 1L })
        {
            var metrics = new DefaultMetricsCollector();
            using var searcher = new IndexSearcher(searchDirectory, new IndexSearcherConfig
            {
                ParallelSearch = false,
                EnableQueryCache = false,
                MaxCachedSegmentReaderBytes = cacheBytes,
                Metrics = metrics,
            });
            var reader = Assert.Single(searcher.GetSegmentReaders());
            Assert.True(Assert.Single(reader.Info.VectorFields).HasGraph);

            for (int q = 0; q < 12; q++)
            {
                var hits = searcher.Search(new VectorQuery("embedding", vectors[q * 50], topK: 5), 5, TestContext.Current.CancellationToken);
                Assert.Equal(5, hits.ScoreDocs.Length);
                Assert.InRange(hits.ScoreDocs[0].Score, 0.999f, 1.001f);
            }

            var snapshot = metrics.GetSnapshot();
            Assert.Equal(12, snapshot.VectorGraphRouteCount);
            Assert.Equal(0, snapshot.VectorGraphFallbackCount);
        }
    }
}

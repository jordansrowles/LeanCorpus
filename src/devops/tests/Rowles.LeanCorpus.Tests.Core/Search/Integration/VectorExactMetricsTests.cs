using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class VectorExactMetricsTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public VectorExactMetricsTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Vector indexing does not emit retired HNSW build metrics")]
    public void VectorIndexing_DoesNotEmitHnswBuildMetrics()
    {
        string path = SubDir(nameof(VectorIndexing_DoesNotEmitHnswBuildMetrics));
        var metrics = new DefaultMetricsCollector();
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { Metrics = metrics }))
        {
            for (int docId = 0; docId < 50; docId++)
            {
                var document = new LeanDocument();
                document.Add(new VectorField("embedding", new float[] { docId + 1f, 1f }));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        MetricsSnapshot snapshot = metrics.GetSnapshot();
        Assert.Equal(0, snapshot.HnswBuildCount);
        Assert.Equal(0, snapshot.HnswNodesBuilt);
    }

    [Fact(DisplayName = "Exact vector search records scan counts and explains its strategy")]
    public void ExactVectorSearch_RecordsScanMetricsAndExplanation()
    {
        string path = SubDir(nameof(ExactVectorSearch_RecordsScanMetricsAndExplanation));
        var metrics = new DefaultMetricsCollector();
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { Metrics = metrics }))
        {
            var random = new Random(123);
            for (int docId = 0; docId < 40; docId++)
            {
                float[] vector = [random.NextSingle(), random.NextSingle()];
                var document = new LeanDocument();
                document.Add(new VectorField("embedding", vector));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig
        {
            EnableQueryCache = false,
            Metrics = metrics,
        });
        var query = new VectorQuery("embedding", [0.2f, 0.7f], topK: 3);
        TopDocs result = searcher.Search(query, 3);
        Assert.Equal(3, result.ScoreDocs.Length);

        Explanation? explanation = searcher.Explain(query, result.ScoreDocs[0].DocId);
        Assert.NotNull(explanation);
        Assert.Contains("exact Float32 scan", explanation!.Description, StringComparison.OrdinalIgnoreCase);

        MetricsSnapshot snapshot = metrics.GetSnapshot();
        Assert.Equal(1, snapshot.VectorExactScanCount);
        Assert.Equal(40, snapshot.VectorExactVectorsScanned);
        Assert.Equal(0, snapshot.VectorRetiredHnswExactScanCount);
        Assert.Equal(0, snapshot.HnswSearchCount);
        Assert.Equal(0, snapshot.HnswNodesVisited);
    }

    private string SubDir(string name)
    {
        string path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }
}

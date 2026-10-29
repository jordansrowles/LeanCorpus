using Rowles.LeanCorpus.Benchmarks;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class HnswBenchmarkQualityTests
{
    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    public void Ef64BenchmarkProfile_MeetsPinnedRecallFloor(int dimension)
    {
        var dataset = BenchmarkVectorData.Get(10_000, dimension);
        var temporaryDirectory = Directory.CreateTempSubdirectory("lc-hnsw-quality-");
        try
        {
            var writerConfig = new IndexWriterConfig
            {
                BuildHnswOnFlush = true,
                NormaliseVectors = true,
                HnswBuildConfig = HnswSearchBenchmarks.CreateGraphBuildConfig(),
                HnswSeed = HnswSearchBenchmarks.GraphSeed,
            };

            using (var writerDirectory = new MMapDirectory(temporaryDirectory.FullName))
            using (var writer = new IndexWriter(writerDirectory, writerConfig))
            {
                foreach (var record in dataset.Records)
                {
                    var document = new LeanDocument();
                    document.Add(new VectorField("emb", new ReadOnlyMemory<float>(record.Vector)));
                    writer.AddDocument(document);
                }
                writer.Commit();
            }

            using var searchDirectory = new MMapDirectory(temporaryDirectory.FullName);
            using var searcher = new IndexSearcher(searchDirectory);
            var measurements = BenchmarkVectorQuality.Measure(
                dataset,
                topK => searcher.Search(
                    new VectorQuery("emb", dataset.Query.Vector, topK: topK, efSearch: 64), topK).ScoreDocs,
                quantised: false);

            BenchmarkVectorQuality.RequireMinimumRecall(
                measurements,
                $"HNSW docCount=10000, dimension={dimension}, efSearch=64");
        }
        finally
        {
            temporaryDirectory.Delete(recursive: true);
        }
    }
}

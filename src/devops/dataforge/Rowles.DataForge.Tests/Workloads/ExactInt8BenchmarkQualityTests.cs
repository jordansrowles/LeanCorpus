using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Benchmarks;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class ExactInt8BenchmarkQualityTests
{
    [Fact]
    public void ExactInt8_10KUniform64QuerySeed2025_MeetsHeldOutQualityFloors()
    {
        BenchmarkVectorDataset querySeed42 = BenchmarkVectorData.Get(
            10_000, 64, VectorDistribution.Uniform, 42, 42, 4);
        BenchmarkVectorDataset querySeed2025 = BenchmarkVectorData.Get(
            10_000, 64, VectorDistribution.Uniform, 42, 2025, 4);
        DirectoryInfo temporaryDirectory = Directory.CreateTempSubdirectory("lc-int8-exact-quality-");
        try
        {
            var writerConfig = new IndexWriterConfig
            {
                BuildHnswOnFlush = false,
                IndexingConcurrency = 1,
                MaxBufferedDocs = querySeed42.Records.Length + 1,
                RamBufferSizeMB = 0,
                RamPerThreadHardLimitMB = 0,
                NormaliseVectors = true,
                VectorQuantisation = VectorQuantisation.Int8,
            };

            using (var writerDirectory = new MMapDirectory(temporaryDirectory.FullName))
            using (var writer = new IndexWriter(writerDirectory, writerConfig))
            {
                foreach (VectorRecord record in querySeed42.Records)
                {
                    var document = new LeanDocument();
                    document.Add(new VectorField("emb", new ReadOnlyMemory<float>(record.Vector)));
                    writer.AddDocument(document);
                }
                writer.Commit();
            }

            using var searchDirectory = new MMapDirectory(temporaryDirectory.FullName);
            using var searcher = new IndexSearcher(searchDirectory);
            foreach (BenchmarkVectorDataset queryDataset in new[] { querySeed42, querySeed2025 })
            {
                VectorQueryQualityResult[] results = queryDataset.Queries.Select(query =>
                    new VectorQueryQualityResult(query.Id, BenchmarkVectorQuality.Measure(
                        queryDataset,
                        query,
                        topK => searcher.Search(
                            new VectorQuery("emb", query.Vector, topK: topK, oversamplingFactor: 1), topK).ScoreDocs,
                        quantised: true))).ToArray();

                BenchmarkVectorData.WriteVectorQualityGroup(
                    queryDataset,
                    "hnsw-exact",
                    "int8-zero-centred-regression",
                    new
                    {
                        quantisation = VectorQuantisation.Int8.ToString(),
                        topK = new[] { 1, 10, 100 },
                        heldOutQueryIds = queryDataset.Queries.Select(static query => query.Id).ToArray(),
                    },
                    results);
                BenchmarkVectorQuality.RequireHeldOutGroup(
                    results,
                    $"Exact Int8, 10K/64D Uniform, corpus seed 42, query seed {queryDataset.QuerySeed}");
            }
        }
        finally
        {
            temporaryDirectory.Delete(recursive: true);
        }
    }
}

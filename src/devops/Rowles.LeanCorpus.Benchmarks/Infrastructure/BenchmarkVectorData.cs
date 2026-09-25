using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.LeanCorpus.Benchmarks;

internal sealed record BenchmarkVectorDataset(
    VectorRecord[] Records,
    VectorQueryCase Query,
    DataForgeDatasetIdentity Identity);

internal static class BenchmarkVectorData
{
    private static readonly ConcurrentDictionary<(int Count, int Dimension), Lazy<BenchmarkVectorDataset>> Cache = new();

    public static BenchmarkVectorDataset Get(int count, int dimension)
    {
        if (count < 1 || dimension < 2)
            throw new ArgumentOutOfRangeException(nameof(count));
        var dataset = Cache.GetOrAdd((count, dimension), static key =>
            new Lazy<BenchmarkVectorDataset>(() => Generate(key.Count, key.Dimension),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        BenchmarkDatasetSidecars.Write(dataset.Identity);
        return dataset;
    }

    public static void WriteHnswRecall(
        BenchmarkVectorDataset dataset,
        int efSearch,
        int topK,
        int recallHits)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var root = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("LEANCORPUS_ARTIFACT_DIR must be set before recording HNSW recall.");
        var directory = Path.Combine(Path.GetFullPath(root), "vector-recall");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{dataset.Identity.GetShortKey()}-ef{efSearch}.json");
        var content = JsonSerializer.SerializeToUtf8Bytes(new
        {
            profileId = dataset.Identity.ProfileId,
            profileVersion = dataset.Identity.ProfileVersion,
            seed = dataset.Identity.Seed,
            recordCount = dataset.Identity.RecordCount,
            parameters = dataset.Identity.Parameters,
            contentSha256 = dataset.Identity.ContentSha256,
            efSearch,
            topK,
            recallHits,
            recallAtK = (double)recallHits / topK
        });
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static BenchmarkVectorDataset Generate(int count, int dimension)
    {
        var profile = new LeanCorpusVectorProfile();
        var options = new DataForgeGenerationOptions(42, count, new Dictionary<string, string>
        {
            ["dimension"] = dimension.ToString(CultureInfo.InvariantCulture),
            ["vectorDistribution"] = VectorDistribution.Uniform.ToString(),
            ["clusterCount"] = Math.Min(8, count).ToString(CultureInfo.InvariantCulture),
            ["queryCount"] = "1"
        });
        var records = profile.Generate(options).ToArray();
        using var writer = new CanonicalJsonWriter(Stream.Null);
        foreach (var record in records)
        {
            profile.CanonicalRecordWriter.Write(writer, record);
            writer.WriteLine();
        }
        var identity = new DataForgeDatasetIdentity(
            DataForgeSourceKind.Generated,
            DataForgeVersions.DataForgeVersion,
            profile.Descriptor.ProfileId,
            profile.Descriptor.ProfileVersion,
            DatasetId: null,
            DatasetVersion: null,
            Seed: options.Seed,
            RecordCount: records.Length,
            Parameters: options.Parameters,
            ContentSha256: Convert.ToHexString(writer.GetSha256()).ToLowerInvariant());
        return new BenchmarkVectorDataset(records, profile.GenerateQuery(options, 0), identity);
    }
}

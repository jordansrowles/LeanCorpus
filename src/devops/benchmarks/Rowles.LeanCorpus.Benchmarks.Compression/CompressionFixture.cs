using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Codecs.StoredFields;

namespace Rowles.LeanCorpus.Benchmarks.Compression;

internal sealed record CompressionMeasurement(
    int SchemaVersion,
    string Classification,
    string Policy,
    string PayloadSelection,
    int OperationsPerInvoke,
    int OriginalBytes,
    int CompressedBytes,
    double CompressionRatio,
    string PayloadSha256,
    string DecodedSha256,
    DataForgeDatasetIdentity Dataset);

internal static class CompressionFixture
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly (byte[] Bytes, DataForgeDatasetIdentity Identity) Corpus = CreateCorpus();

    public static byte[] BuildPayload(int size)
    {
        if (size < 1 || size > Corpus.Bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(size));
        return Corpus.Bytes.AsSpan(0, size).ToArray();
    }

    public static CompressionMeasurement Measure(FieldCompressionPolicy policy, byte[] payload, byte[] compressed, byte[] decoded)
    {
        if (payload.Length == 0 || compressed.Length == 0 || !payload.AsSpan().SequenceEqual(decoded))
            throw new InvalidDataException("Compression preflight requires non-empty input/output and an exact decoded-byte match.");
        return new CompressionMeasurement(1, "Directional", policy.ToString(), "utf8-prefix", 1,
            payload.Length, compressed.Length, (double)payload.Length / compressed.Length,
            Convert.ToHexStringLower(SHA256.HashData(payload)),
            Convert.ToHexStringLower(SHA256.HashData(decoded)), Corpus.Identity);
    }

    public static void WriteEvidence(string artifactDirectory, CompressionMeasurement measurement)
    {
        var directory = Path.Combine(artifactDirectory, "correctness");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"compression-{measurement.Policy}-{measurement.OriginalBytes}.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(measurement, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static (byte[], DataForgeDatasetIdentity) CreateCorpus()
    {
        var profile = new RowlesTextMultilingualProfile();
        var options = new DataForgeGenerationOptions(42, 1024,
            new Dictionary<string, string> { ["language"] = "en" });
        var records = profile.Generate(options).ToArray();
        using var writer = new CanonicalJsonWriter(Stream.Null);
        foreach (var record in records)
        {
            profile.CanonicalRecordWriter.Write(writer, record);
            writer.WriteLine();
        }
        var identity = new DataForgeDatasetIdentity(DataForgeSourceKind.Generated,
            DataForgeVersions.DataForgeVersion, profile.Descriptor.ProfileId, profile.Descriptor.ProfileVersion,
            DatasetId: null, DatasetVersion: null, options.Seed, records.Length, options.Parameters,
            profile.Dependencies, Convert.ToHexStringLower(writer.GetSha256()));
        return (Encoding.UTF8.GetBytes(string.Join('\n', records.Select(static record => record.Text))), identity);
    }
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.LeanCorpus.Benchmarks;

internal static class TextBenchmarkData
{
    public const int DefaultDocCount = 20_000;

    private const string ArtifactDirectoryVariable = "LEANCORPUS_ARTIFACT_DIR";
    private static readonly ConcurrentDictionary<int, Lazy<string[]>> Documents = new();

    public static IEnumerable<int> GetDocCounts(int defaultCount)
    {
        var configured = Environment.GetEnvironmentVariable("BENCH_DOC_COUNT");
        if (int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0)
            return [count];
        return [defaultCount];
    }

    public static string[] BuildDocuments(int count)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        return Documents.GetOrAdd(count, static requestedCount =>
            new Lazy<string[]>(() => GenerateSearchDocuments(requestedCount),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public static string[] BuildMultilingualInputs(string language, int count)
    {
        var profile = new RowlesTextMultilingualProfile();
        var options = new DataForgeGenerationOptions(42, count,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["language"] = language });
        var records = profile.Generate(options).ToArray();
        WriteIdentity(profile, options, records);
        return records.Select(static record => record.Text).ToArray();
    }

    private static string[] GenerateSearchDocuments(int count)
    {
        var profile = new LeanCorpusSearchProfile();
        var options = new DataForgeGenerationOptions(42, count);
        var records = profile.Generate(options).ToArray();
        WriteIdentity(profile, options, records);
        return records.Select(static record => record.Body).ToArray();
    }

    private static void WriteIdentity<TRecord>(
        IDataForgeGeneratedProfile<TRecord> profile,
        DataForgeGenerationOptions options,
        IReadOnlyList<TRecord> records)
    {
        var artifactRoot = Environment.GetEnvironmentVariable(ArtifactDirectoryVariable);
        if (string.IsNullOrWhiteSpace(artifactRoot))
            throw new InvalidOperationException($"{ArtifactDirectoryVariable} must be set before benchmark data is generated.");

        using var canonicalStream = new MemoryStream();
        using (var writer = new CanonicalJsonWriter(canonicalStream))
        {
            foreach (var record in records)
            {
                profile.CanonicalRecordWriter.Write(writer, record);
                writer.WriteLine();
            }
        }

        var identity = new DataForgeDatasetIdentity(
            DataForgeSourceKind.Generated,
            DataForgeVersions.DataForgeVersion,
            profile.Descriptor.ProfileId,
            profile.Descriptor.ProfileVersion,
            DatasetId: null,
            DatasetVersion: null,
            Seed: options.Seed,
            RecordCount: records.Count,
            Parameters: options.Parameters,
            ContentSha256: Convert.ToHexString(SHA256.HashData(canonicalStream.ToArray())).ToLowerInvariant());
        var identityBytes = identity.GetCanonicalBytes();
        var directory = Path.Combine(Path.GetFullPath(artifactRoot), "dataforge");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, string.Concat(identity.GetShortKey(), ".json"));
        if (File.Exists(path))
        {
            EnsureSameIdentity(path, identityBytes);
            return;
        }

        var temporaryPath = Path.Combine(directory,
            string.Concat(".", identity.GetShortKey(), ".", Guid.NewGuid().ToString("N"), ".tmp"));
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(identityBytes);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                EnsureSameIdentity(path, identityBytes);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void EnsureSameIdentity(string path, byte[] expected)
    {
        if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(expected))
            throw new InvalidDataException($"DataForge text benchmark sidecar '{path}' conflicts with its canonical identity.");
    }
}

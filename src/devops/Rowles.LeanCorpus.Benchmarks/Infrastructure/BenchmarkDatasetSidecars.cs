using System.Text.Json;
using Rowles.DataForge;

namespace Rowles.LeanCorpus.Benchmarks;

internal sealed record BenchmarkDatasetEvidence(
    DataForgeDatasetIdentity Identity,
    BenchmarkDatasetReport Report,
    byte[] CanonicalIdentityBytes);

internal static class BenchmarkDatasetSidecars
{
    private const string ArtifactDirectoryVariable = "LEANCORPUS_ARTIFACT_DIR";
    private static readonly string[] IdentityPropertyOrder =
    [
        "sourceKind",
        "dataForgeVersion",
        "profileId",
        "profileVersion",
        "datasetId",
        "datasetVersion",
        "seed",
        "recordCount",
        "parameters",
        "dependencies",
        "contentSha256"
    ];

    public static void Write(DataForgeDatasetIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var artifactRoot = Environment.GetEnvironmentVariable(ArtifactDirectoryVariable);
        if (string.IsNullOrWhiteSpace(artifactRoot))
            throw new InvalidOperationException($"{ArtifactDirectoryVariable} must be set before benchmark data is generated.");

        var directory = Path.Combine(Path.GetFullPath(artifactRoot), "dataforge");
        Directory.CreateDirectory(directory);
        var key = identity.GetShortKey();
        var path = Path.Combine(directory, string.Concat(key, ".json"));
        var content = identity.GetCanonicalBytes();

        if (File.Exists(path))
        {
            EnsureSameContent(path, content, key);
            return;
        }

        var temporaryPath = Path.Combine(directory, string.Concat(".", key, ".", Guid.NewGuid().ToString("N"), ".tmp"));
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
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                EnsureSameContent(path, content, key);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static IReadOnlyList<BenchmarkDatasetEvidence> ReadAll(string artifactDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
        var directory = Path.Combine(Path.GetFullPath(artifactDirectory), "dataforge");
        if (!Directory.Exists(directory))
            return [];

        var byIdentity = new Dictionary<string, BenchmarkDatasetEvidence>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(static file => Path.GetFileName(file), StringComparer.Ordinal))
        {
            var bytes = File.ReadAllBytes(path);
            var identity = ReadIdentity(bytes);
            var canonicalBytes = identity.GetCanonicalBytes();
            if (!bytes.AsSpan().SequenceEqual(canonicalBytes))
                throw new InvalidDataException($"DataForge sidecar '{path}' is not canonical or does not match its identity.");

            var expectedFileName = string.Concat(identity.GetShortKey(), ".json");
            if (!string.Equals(Path.GetFileName(path), expectedFileName, StringComparison.Ordinal))
                throw new InvalidDataException($"DataForge sidecar '{path}' does not match its canonical identity key '{expectedFileName}'.");

            var canonicalKey = Convert.ToHexString(canonicalBytes);
            byIdentity.TryAdd(canonicalKey, new BenchmarkDatasetEvidence(
                identity,
                ToReport(identity),
                canonicalBytes));
        }

        return byIdentity.Values
            .OrderBy(static evidence => Convert.ToHexString(evidence.CanonicalIdentityBytes), StringComparer.Ordinal)
            .ToArray();
    }

    private static DataForgeDatasetIdentity ReadIdentity(ReadOnlyMemory<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.EnumerateObject().Select(static property => property.Name)
                .SequenceEqual(IdentityPropertyOrder, StringComparer.Ordinal))
        {
            throw new InvalidDataException("DataForge dataset sidecar has an invalid identity shape or property order.");
        }

        var sourceKind = root.GetProperty("sourceKind").GetString() switch
        {
            "Generated" => DataForgeSourceKind.Generated,
            "Imported" => DataForgeSourceKind.Imported,
            var value => throw new InvalidDataException($"Unsupported DataForge source kind '{value}'.")
        };
        var parametersElement = root.GetProperty("parameters");
        if (parametersElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("DataForge dataset sidecar parameters must be an object.");

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in parametersElement.EnumerateObject())
        {
            var value = property.Value.GetString() ?? throw new InvalidDataException("DataForge dataset parameters must be strings.");
            if (!parameters.TryAdd(property.Name, value))
                throw new InvalidDataException($"DataForge dataset sidecar contains a duplicate parameter '{property.Name}'.");
        }

        var dependenciesElement = root.GetProperty("dependencies");
        if (dependenciesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("DataForge dataset sidecar dependencies must be an array.");
        var dependencies = dependenciesElement.EnumerateArray().Select(static item =>
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.EnumerateObject().Select(static property => property.Name)
                    .SequenceEqual(["name", "version"], StringComparer.Ordinal))
                throw new InvalidDataException("DataForge dataset dependency has an invalid shape or property order.");
            return new DataForgeDependencyVersion(
                item.GetProperty("name").GetString() ?? throw new InvalidDataException("DataForge dependency name must be a string."),
                item.GetProperty("version").GetString() ?? throw new InvalidDataException("DataForge dependency version must be a string."));
        }).ToArray();

        var seedElement = root.GetProperty("seed");
        ulong? seed = seedElement.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number when seedElement.TryGetUInt64(out var value) => value,
            _ => throw new InvalidDataException("DataForge dataset sidecar seed must be an unsigned integer or null.")
        };

        return new DataForgeDatasetIdentity(
            sourceKind,
            root.GetProperty("dataForgeVersion").GetInt32(),
            ReadNullableString(root.GetProperty("profileId")),
            ReadNullableInt32(root.GetProperty("profileVersion")),
            ReadNullableString(root.GetProperty("datasetId")),
            ReadNullableInt32(root.GetProperty("datasetVersion")),
            seed,
            root.GetProperty("recordCount").GetInt32(),
            parameters,
            dependencies,
            root.GetProperty("contentSha256").GetString() ?? throw new InvalidDataException("DataForge dataset content hash must be a string."));
    }

    private static BenchmarkDatasetReport ToReport(DataForgeDatasetIdentity identity)
        => new()
        {
            SourceKind = identity.SourceKind.ToString(),
            DataForgeVersion = identity.DataForgeVersion,
            ProfileId = identity.ProfileId,
            ProfileVersion = identity.ProfileVersion,
            DatasetId = identity.DatasetId,
            DatasetVersion = identity.DatasetVersion,
            Seed = identity.Seed,
            RecordCount = identity.RecordCount,
            Parameters = identity.Parameters.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            Dependencies = identity.Dependencies.ToArray(),
            ContentSha256 = identity.ContentSha256
        };

    private static string? ReadNullableString(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => throw new InvalidDataException("DataForge dataset sidecar expected a string or null.")
        };

    private static int? ReadNullableInt32(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number => value.GetInt32(),
            _ => throw new InvalidDataException("DataForge dataset sidecar expected an integer or null.")
        };

    private static void EnsureSameContent(string path, byte[] expected, string key)
    {
        var existing = File.ReadAllBytes(path);
        if (!existing.AsSpan().SequenceEqual(expected))
            throw new InvalidDataException($"DataForge sidecar key collision or conflicting content for '{key}'.");
    }
}

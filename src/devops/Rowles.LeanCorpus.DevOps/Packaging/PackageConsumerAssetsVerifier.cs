using System.Security.Cryptography;
using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Packaging;

internal sealed record VerifiedConsumerPackage(string PackageId, string Version, string Sha256);

internal static class PackageConsumerAssetsVerifier
{
    public static IReadOnlyList<VerifiedConsumerPackage> Verify(
        string assetsPath,
        string cacheDirectory,
        ConsumerScenario scenario,
        PackageManifestIndex packageIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(packageIndex);
        if (!File.Exists(assetsPath))
        {
            throw new InvalidDataException($"Restore did not create {assetsPath}");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new InvalidDataException($"Consumer restore assets are not valid JSON: {assetsPath}", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("project", out var project) ||
                !project.TryGetProperty("restore", out var restore) ||
                !restore.TryGetProperty("projectStyle", out var style) ||
                style.GetString() != "PackageReference")
            {
                throw new InvalidDataException($"Consumer restore style was not PackageReference in {assetsPath}.");
            }
            if (!root.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Consumer restore assets have no package library map: {assetsPath}");
            }

            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var library in libraries.EnumerateObject())
            {
                var separator = library.Name.IndexOf('/');
                if (separator <= 0)
                {
                    continue;
                }
                var id = library.Name[..separator];
                if (!IsInternalPackage(id))
                {
                    continue;
                }
                var version = library.Name[(separator + 1)..];
                if (resolved.TryGetValue(id, out var previousVersion) && !string.Equals(previousVersion, version, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Consumer resolved multiple versions for internal package {id}.");
                }
                resolved[id] = version;

                if (!packageIndex.Packages.TryGetValue(id, out var manifestPackage))
                {
                    throw new InvalidDataException($"Consumer resolved internal package {id}, which is absent from the package manifest.");
                }
                if (!string.Equals(manifestPackage.Version, version, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Consumer resolved {id} {version}, but the package manifest records {manifestPackage.Version}.");
                }

                var cachedArchive = GetCachedArchivePath(cacheDirectory, id, version);
                using var stream = File.OpenRead(cachedArchive);
                var cachedHash = Convert.ToHexStringLower(SHA256.HashData(stream));
                if (!string.Equals(cachedHash, manifestPackage.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Consumer cache archive hash does not match the manifest for {id} {version}.");
                }
            }

            foreach (var id in scenario.PackageIds)
            {
                if (!resolved.ContainsKey(id))
                {
                    throw new InvalidDataException($"Consumer assets do not contain required package {id}.");
                }
            }

            if (scenario.ExactInternalIds.Count > 0)
            {
                var actualIds = resolved.Keys.Order(StringComparer.Ordinal).ToArray();
                var expectedIds = scenario.ExactInternalIds.Order(StringComparer.Ordinal).ToArray();
                if (!actualIds.SequenceEqual(expectedIds, StringComparer.Ordinal))
                {
                    throw new InvalidDataException($"Unexpected internal package graph for {scenario.Name}: expected [{string.Join(", ", expectedIds)}], found [{string.Join(", ", actualIds)}].");
                }
            }

            if (scenario.RuntimeKind == "compression" && resolved.Keys.Any(static id =>
                id.Equals("Rowles.Text", StringComparison.Ordinal) || id.StartsWith("Rowles.LeanCorpus.", StringComparison.Ordinal)))
            {
                throw new InvalidDataException("Compression consumer resolved an unexpected Rowles.Text or renamed Core dependency.");
            }

            return resolved.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new VerifiedConsumerPackage(pair.Key, pair.Value, packageIndex.Packages[pair.Key].Sha256))
                .ToArray();
        }
    }

    private static string GetCachedArchivePath(string cacheDirectory, string packageId, string version)
    {
        var packageRoot = Path.Combine(cacheDirectory, packageId.ToLowerInvariant(), version.ToLowerInvariant());
        if (!Directory.Exists(packageRoot))
        {
            throw new InvalidDataException($"Isolated cache does not contain {packageId} {version}: {packageRoot}");
        }
        var expectedFile = $"{packageId.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg";
        var archive = Directory.EnumerateFiles(packageRoot, "*.nupkg")
            .SingleOrDefault(path => string.Equals(Path.GetFileName(path), expectedFile, StringComparison.Ordinal));
        return archive ?? throw new InvalidDataException($"Isolated cache archive is missing for {packageId} {version}: {packageRoot}");
    }

    private static bool IsInternalPackage(string id)
        => id.Equals("LeanCorpus", StringComparison.Ordinal) || id.StartsWith("LeanCorpus.", StringComparison.Ordinal) ||
            id.Equals("Rowles.Text", StringComparison.Ordinal) || id.StartsWith("Rowles.LeanCorpus.", StringComparison.Ordinal);
}

using System.Security.Cryptography;
using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Packaging;

internal sealed record PackageManifestPackage(string PackageId, string Version, string Path, string RelativePath, string Sha256);

internal sealed record PackageManifestIndex(string SourceCommit, IReadOnlyDictionary<string, PackageManifestPackage> Packages);

internal static class PackageManifestReader
{
    public static PackageManifestIndex Read(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageDirectory));
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException($"Package manifest is missing: {manifestPath}");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new InvalidDataException($"Package manifest is not valid JSON: {manifestPath}", exception);
        }

        using (document)
        {
            var rootElement = document.RootElement;
            if (rootElement.ValueKind != JsonValueKind.Object ||
                !rootElement.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1 ||
                !rootElement.TryGetProperty("sourceCommit", out var commitElement) ||
                string.IsNullOrWhiteSpace(commitElement.GetString()))
            {
                throw new InvalidDataException($"Package manifest has an unsupported schema or missing sourceCommit: {manifestPath}");
            }
            if (!rootElement.TryGetProperty("packages", out var packagesElement) || packagesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"Package manifest has no package array: {manifestPath}");
            }

            var packages = new Dictionary<string, PackageManifestPackage>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in packagesElement.EnumerateArray())
            {
                var relativePath = GetString(item, "path");
                if (!relativePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = GetString(item, "packageId");
                var version = GetString(item, "version");
                var hash = GetString(item, "sha256").ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version) || !IsSha256(hash) || packages.ContainsKey(id))
                {
                    throw new InvalidDataException($"Package manifest has an empty or duplicate package ID '{id}', version, or SHA-256 value.");
                }

                var normalisedRelative = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                var packagePath = Path.GetFullPath(Path.Combine(root, normalisedRelative));
                var relativeToRoot = Path.GetRelativePath(root, packagePath);
                if (Path.IsPathRooted(relativeToRoot) || relativeToRoot == ".." || relativeToRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Manifest package path resolves outside the package directory: '{relativePath}'.");
                }
                if (!File.Exists(packagePath))
                {
                    throw new InvalidDataException($"Manifest package archive is missing: {packagePath}");
                }

                using var stream = File.OpenRead(packagePath);
                var actualHash = Convert.ToHexStringLower(SHA256.HashData(stream));
                if (!string.Equals(actualHash, hash, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Package manifest SHA-256 does not match {id} {version}.");
                }
                packages.Add(id, new PackageManifestPackage(id, version, packagePath, relativePath, hash));
            }

            return new PackageManifestIndex(commitElement.GetString()!, packages);
        }
    }

    private static string GetString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

using System.Security.Cryptography;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Packaging;

namespace Rowles.LeanCorpus.DevOps.Tests.Packaging;

public sealed class PackageConsumerAssetsVerifierTests
{
    [Fact]
    public void VerifiesExactInternalPackageGraphAndCachedArchiveHashes()
    {
        using var directory = TemporaryConsumerDirectory.Create();
        var core = directory.AddPackage("LeanCorpus", "4.0.0", "core archive");
        var codec = directory.AddPackage("LeanCorpus.Compression.LZ4", "1.0.1", "codec archive");
        var scenario = CreateCompressionScenario(codec.PackageId);
        var assets = directory.WriteAssets(["LeanCorpus/4.0.0", "LeanCorpus.Compression.LZ4/1.0.1"]);

        var verified = PackageConsumerAssetsVerifier.Verify(assets, directory.CacheRoot, scenario,
            new PackageManifestIndex("abc123", new Dictionary<string, PackageManifestPackage>(StringComparer.OrdinalIgnoreCase)
            {
                [core.PackageId] = core,
                [codec.PackageId] = codec
            }));

        Assert.Equal(new[] { "LeanCorpus", "LeanCorpus.Compression.LZ4" }, verified.Select(static item => item.PackageId).Order(StringComparer.Ordinal));
        Assert.Equal(core.Sha256, verified.Single(item => item.PackageId == core.PackageId).Sha256);
        Assert.Equal(codec.Sha256, verified.Single(item => item.PackageId == codec.PackageId).Sha256);
    }

    [Fact]
    public void RejectsAnOlderCoreVersionEvenWhenItIsPresentInTheRestoreGraph()
    {
        using var directory = TemporaryConsumerDirectory.Create();
        var core = directory.AddPackage("LeanCorpus", "4.0.0", "current core archive");
        var codec = directory.AddPackage("LeanCorpus.Compression.LZ4", "1.0.1", "codec archive");
        directory.AddCachedPackage("LeanCorpus", "3.1.1", "older core archive");
        directory.AddCachedPackage("LeanCorpus.Compression.LZ4", "1.0.1", "codec archive");
        var scenario = CreateCompressionScenario(codec.PackageId);
        var assets = directory.WriteAssets(["LeanCorpus/3.1.1", "LeanCorpus.Compression.LZ4/1.0.1"]);
        var packageIndex = new PackageManifestIndex("abc123", new Dictionary<string, PackageManifestPackage>(StringComparer.OrdinalIgnoreCase)
        {
            [core.PackageId] = core,
            [codec.PackageId] = codec
        });

        var exception = Assert.Throws<InvalidDataException>(() =>
            PackageConsumerAssetsVerifier.Verify(assets, directory.CacheRoot, scenario, packageIndex));

        Assert.Contains("manifest records", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ConsumerScenario CreateCompressionScenario(string packageId)
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, ".config/devops/package-policy.json")))
                continue;
            var policy = PackagePolicyReader.Read(Path.Combine(directory.FullName, ".config/devops/package-policy.json"), directory.FullName);
            return ConsumerScenarioCatalog.Create(policy, "compression", directory.FullName).First(item => item.PackageId == packageId);
        }

        throw new DirectoryNotFoundException("The LeanCorpus package policy was not found from the current test directory.");
    }

    private sealed class TemporaryConsumerDirectory : IDisposable
    {
        private TemporaryConsumerDirectory(string root)
        {
            Root = root;
            CacheRoot = Path.Combine(root, "isolated cache");
            Directory.CreateDirectory(CacheRoot);
        }

        public string Root { get; }

        public string CacheRoot { get; }

        public static TemporaryConsumerDirectory Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "leancorpus-consumer-assets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TemporaryConsumerDirectory(root);
        }

        public PackageManifestPackage AddPackage(string id, string version, string content)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var packageRoot = Path.Combine(CacheRoot, id.ToLowerInvariant(), version.ToLowerInvariant());
            Directory.CreateDirectory(packageRoot);
            File.WriteAllBytes(Path.Combine(packageRoot, $"{id.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg"), bytes);
            return new PackageManifestPackage(id, version, Path.Combine(Root, $"{id}.{version}.nupkg"), $"{id}.{version}.nupkg", hash);
        }

        public void AddCachedPackage(string id, string version, string content)
        {
            var packageRoot = Path.Combine(CacheRoot, id.ToLowerInvariant(), version.ToLowerInvariant());
            Directory.CreateDirectory(packageRoot);
            File.WriteAllText(Path.Combine(packageRoot, $"{id.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg"), content);
        }

        public string WriteAssets(IReadOnlyList<string> libraryKeys)
        {
            var path = Path.Combine(Root, "project.assets.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                project = new { restore = new { projectStyle = "PackageReference" } },
                libraries = libraryKeys.ToDictionary(static key => key, static _ => new { type = "package" })
            }));
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

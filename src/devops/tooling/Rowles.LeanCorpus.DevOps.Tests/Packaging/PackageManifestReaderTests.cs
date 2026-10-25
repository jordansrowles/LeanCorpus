using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Packaging;

namespace Rowles.LeanCorpus.DevOps.Tests.Packaging;

public sealed class PackageManifestReaderTests
{
    [Fact]
    public void RejectsPackageArchivesThatDoNotMatchTheManifestHash()
    {
        using var directory = new TemporaryPackageDirectory();
        var packagePath = Path.Combine(directory.Path, "LeanCorpus.4.0.0.nupkg");
        File.WriteAllText(packagePath, "changed package bytes");
        WriteManifest(directory.Path, "LeanCorpus.4.0.0.nupkg", new string('0', 64));

        var exception = Assert.Throws<InvalidDataException>(() => PackageManifestReader.Read(directory.Path));

        Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IndexesOnlyExistingManifestPackagesWithMatchingContentHashes()
    {
        using var directory = new TemporaryPackageDirectory();
        var packagePath = Path.Combine(directory.Path, "LeanCorpus.4.0.0.nupkg");
        var bytes = Encoding.UTF8.GetBytes("package archive bytes");
        File.WriteAllBytes(packagePath, bytes);
        WriteManifest(directory.Path, "LeanCorpus.4.0.0.nupkg", Convert.ToHexStringLower(SHA256.HashData(bytes)));

        var index = PackageManifestReader.Read(directory.Path);

        Assert.Equal("abc123", index.SourceCommit);
        Assert.Equal("4.0.0", index.Packages["LeanCorpus"].Version);
        Assert.Equal(packagePath, index.Packages["LeanCorpus"].Path);
    }

    private static void WriteManifest(string directory, string archiveName, string hash)
    {
        var manifest = new
        {
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.Parse("2026-10-21T12:00:00Z"),
            sourceCommit = "abc123",
            configuration = "Release",
            packages = new[]
            {
                new { packageId = "LeanCorpus", version = "4.0.0", path = archiveName, size = 20L, sha256 = hash }
            }
        };
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest));
    }

    private sealed class TemporaryPackageDirectory : IDisposable
    {
        public TemporaryPackageDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "leancorpus-package-manifest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

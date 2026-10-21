using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Data;

namespace Rowles.LeanCorpus.DevOps.Tests.Data;

public sealed class MigrationFixtureGenerationServiceTests
{
    [Fact]
    public void BuildsARepeatableArchiveFromExactlyTheHashedManifestFiles()
    {
        using var directory = new TemporaryDirectory();
        var layoutDirectory = Path.Combine(directory.Path, "loose");
        Directory.CreateDirectory(layoutDirectory);
        File.WriteAllText(Path.Combine(layoutDirectory, "b.seg"), "segment b");
        File.WriteAllText(Path.Combine(layoutDirectory, "a.seg"), "segment a");
        var manifestPath = Path.Combine(directory.Path, "manifest.json");
        File.WriteAllText(manifestPath, CreateManifest(layoutDirectory, "a.seg", "b.seg"));

        var first = MigrationFixtureGenerationService.CreateArchive(layoutDirectory, manifestPath);
        var second = MigrationFixtureGenerationService.CreateArchive(layoutDirectory, manifestPath);

        Assert.Equal(first, second);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(first)), MigrationFixtureGenerationService.Hash(first));
        using (var archiveInput = new MemoryStream(first))
        using (var archiveGzip = new GZipStream(archiveInput, CompressionMode.Decompress))
        using (var tarBytes = new MemoryStream())
        {
            archiveGzip.CopyTo(tarBytes);
            Assert.Equal(0, tarBytes.Length % 10_240);
        }
        using var input = new MemoryStream(first);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        var firstEntry = tar.GetNextEntry();
        var secondEntry = tar.GetNextEntry();
        Assert.Equal("a.seg", firstEntry?.Name);
        Assert.Equal("b.seg", secondEntry?.Name);
        Assert.Null(tar.GetNextEntry());
    }

    [Fact]
    public void RejectsAFileThatDoesNotMatchItsManifestHash()
    {
        using var directory = new TemporaryDirectory();
        var layoutDirectory = Path.Combine(directory.Path, "loose");
        Directory.CreateDirectory(layoutDirectory);
        File.WriteAllText(Path.Combine(layoutDirectory, "a.seg"), "actual content");
        var manifestPath = Path.Combine(directory.Path, "manifest.json");
        File.WriteAllText(manifestPath, """
            {"Files":[{"Name":"a.seg","Length":7,"Sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]}
            """);

        Assert.Throws<InvalidDataException>(() => MigrationFixtureGenerationService.CreateArchive(layoutDirectory, manifestPath));
    }

    private static string CreateManifest(string layoutDirectory, params string[] names)
    {
        var files = names.Select(name =>
        {
            var bytes = File.ReadAllBytes(Path.Combine(layoutDirectory, name));
            return new { Name = name, Length = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
        });
        return JsonSerializer.Serialize(new { Files = files }, new JsonSerializerOptions { WriteIndented = true });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "leancorpus-migration-fixture-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Distribution;

namespace Rowles.LeanCorpus.DevOps.Tests.Distribution;

public sealed class ReleaseArchiveBuilderTests
{
    [Fact]
    public async Task CreatesSingleFileArchiveWithSourceIdentityNoticesAndChecksums()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "DevOps release archive " + Guid.NewGuid().ToString("N"));
        var repositoryRoot = Path.Combine(root, "checkout");
        var projectRoot = Path.Combine(repositoryRoot, "src", "devops", "Rowles.LeanCorpus.DevOps");
        var publishDirectory = Path.Combine(root, "publish");
        var outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, "Licenses"));
        Directory.CreateDirectory(publishDirectory);
        await File.WriteAllTextAsync(Path.Combine(repositoryRoot, "LICENCE"), "project licence", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "RELEASE-ARCHIVE-README.md"), "release prerequisites", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "THIRD-PARTY-NOTICES.md"), "third-party notices", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Licenses", "libgit2.license.txt"), "native library licence", cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(publishDirectory, OperatingSystem.IsWindows() ? "devops.exe" : "devops"), [1, 2, 3, 4, 5], cancellationToken);

        try
        {
            var result = await new ReleaseArchiveBuilder().BuildAsync(new ReleaseArchiveRequest
            {
                RepositoryRoot = repositoryRoot,
                ProjectRoot = projectRoot,
                PublishDirectory = publishDirectory,
                OutputDirectory = outputDirectory,
                ProductVersion = "1.0.0",
                SourceCommit = "0123456789abcdef0123456789abcdef01234567",
                SourceBranch = "issue-97",
                SourceDirty = false,
                RuntimeIdentifier = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64",
                SelfContained = false,
                PublishRunId = Guid.Parse("be3f71c5-3183-49a8-a736-b0800e96c4c9")
            }, cancellationToken);

            Assert.True(File.Exists(result.ArchivePath));
            Assert.True(File.Exists(result.ChecksumPath));
            Assert.Contains("devops-1.0.0-", Path.GetFileName(result.ArchivePath), StringComparison.Ordinal);
            using var archive = ZipFile.OpenRead(result.ArchivePath);
            var names = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
            Assert.Contains(OperatingSystem.IsWindows() ? "devops.exe" : "devops", names);
            Assert.Contains("LICENCE", names);
            Assert.Contains("README.md", names);
            Assert.Contains("THIRD-PARTY-NOTICES.md", names);
            Assert.Contains("licenses/libgit2.license.txt", names);
            Assert.Contains("distribution-manifest.json", names);
            Assert.Contains("SHA256SUMS", names);

            using var manifestStream = archive.GetEntry("distribution-manifest.json")!.Open();
            using var manifest = await JsonDocument.ParseAsync(manifestStream, cancellationToken: cancellationToken);
            Assert.Equal("0123456789abcdef0123456789abcdef01234567", manifest.RootElement.GetProperty("sourceCommit").GetString());
            Assert.Equal("1.0.0", manifest.RootElement.GetProperty("productVersion").GetString());
            Assert.Equal(OperatingSystem.IsWindows() ? "win-x64" : "linux-x64", manifest.RootElement.GetProperty("runtimeIdentifier").GetString());
            Assert.False(manifest.RootElement.GetProperty("selfContained").GetBoolean());
            Assert.Equal("be3f71c5-3183-49a8-a736-b0800e96c4c9", manifest.RootElement.GetProperty("publishRunId").GetString());

            await using var archiveStream = File.OpenRead(result.ArchivePath);
            var archiveHash = Convert.ToHexString(await SHA256.HashDataAsync(archiveStream, cancellationToken)).ToLowerInvariant();
            Assert.Contains(archiveHash, await File.ReadAllTextAsync(result.ChecksumPath, cancellationToken), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

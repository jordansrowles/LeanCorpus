using System.IO.Compression;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Distribution;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Distribution;

public sealed class ReleaseDistributionServiceTests
{
    [Fact]
    public async Task BuildsArchiveFromTheActualRepositoryProjectRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var artifactPaths = new ArtifactPaths(repositoryRoot);
        var processRunner = new PublishingProcessRunner();
        var service = new ReleaseDistributionService(
            artifactPaths,
            new ArtifactRunStore(artifactPaths, runCategory: "distribution"),
            processRunner,
            new FakeSdkResolver(),
            new FakeProvenanceReader());
        var request = new ReleaseDistributionRequest
        {
            Checkout = new CheckoutIdentity(repositoryRoot, null, null),
            RuntimeIdentifier = "linux-x64"
        };

        ReleaseDistributionResult? result = null;
        try
        {
            result = await service.BuildAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
            Assert.NotNull(result.Archive);
            Assert.True(File.Exists(result.Archive.ArchivePath));
            Assert.True(File.Exists(result.Archive.ChecksumPath));

            var projectRoot = Path.Combine(repositoryRoot, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps");
            using var archive = ZipFile.OpenRead(result.Archive.ArchivePath);
            var entries = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("README.md", entries);
            Assert.Contains("THIRD-PARTY-NOTICES.md", entries);
            Assert.Contains("LICENCE", entries);
            Assert.Contains("devops", entries);
            Assert.Contains(entries, name => name.StartsWith("licenses/", StringComparison.Ordinal));
            Assert.Equal(
                await File.ReadAllTextAsync(Path.Combine(projectRoot, "RELEASE-ARCHIVE-README.md"), TestContext.Current.CancellationToken),
                await ReadEntryAsync(archive, "README.md", TestContext.Current.CancellationToken));

            using var manifestStream = archive.GetEntry("distribution-manifest.json")!.Open();
            using var manifest = await JsonDocument.ParseAsync(manifestStream, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("0123456789abcdef0123456789abcdef01234567", manifest.RootElement.GetProperty("sourceCommit").GetString());
            Assert.Equal("issue-97", manifest.RootElement.GetProperty("sourceBranch").GetString());
        }
        finally
        {
            DeleteRunDirectory(artifactPaths.GetRunRoot("distribution"), result?.RunDirectory);
            DeleteRunDirectory(artifactPaths.BuildRunRoot, processRunner.BuildRunDirectory);
        }
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string name, CancellationToken cancellationToken)
    {
        await using var stream = archive.GetEntry(name)!.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "LICENCE")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps", "Rowles.LeanCorpus.DevOps.csproj")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the LeanCorpus checkout for the release distribution integration test.");
    }

    private static void DeleteRunDirectory(string runRoot, string? runDirectory)
    {
        if (string.IsNullOrWhiteSpace(runDirectory))
        {
            return;
        }

        var fullRunRoot = Path.GetFullPath(runRoot);
        var fullRunDirectory = Path.GetFullPath(runDirectory);
        if (!string.Equals(Path.GetDirectoryName(fullRunDirectory), fullRunRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !Guid.TryParse(Path.GetFileName(fullRunDirectory), out _))
        {
            throw new InvalidOperationException($"Refusing to remove an unexpected artefact run path '{runDirectory}'.");
        }

        if (Directory.Exists(fullRunDirectory))
        {
            Directory.Delete(fullRunDirectory, recursive: true);
        }
    }

    private sealed class PublishingProcessRunner : IProcessRunner
    {
        public string? BuildRunDirectory { get; private set; }

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            var outputIndex = request.Arguments.ToList().IndexOf("-o");
            if (outputIndex < 0 || outputIndex + 1 >= request.Arguments.Count)
            {
                throw new InvalidOperationException("The distribution publish request did not specify an output directory.");
            }

            var outputDirectory = request.Arguments[outputIndex + 1];
            Directory.CreateDirectory(outputDirectory);
            await File.WriteAllBytesAsync(Path.Combine(outputDirectory, "devops"), [1, 2, 3, 4], cancellationToken);
            BuildRunDirectory = Path.GetDirectoryName(request.StandardOutputLogPath);
            return new ProcessResult(
                OutcomeKind.Success,
                0,
                [],
                [],
                0,
                0,
                TimeSpan.FromMilliseconds(5),
                request.StandardOutputLogPath,
                request.StandardErrorLogPath,
                null);
        }
    }

    private sealed class FakeSdkResolver : IDotNetSdkResolver
    {
        public Task<SdkResolution> ResolveAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new SdkResolution(OutcomeKind.Success, "11.0.100-preview.7.26381.103", "11.0.100-preview.7.26381.103", "latestPatch", null));
    }

    private sealed class FakeProvenanceReader : IRepositoryProvenanceReader
    {
        public Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new GitProvenance(OutcomeKind.Success, "0123456789abcdef0123456789abcdef01234567", "issue-97", false, null));
    }
}

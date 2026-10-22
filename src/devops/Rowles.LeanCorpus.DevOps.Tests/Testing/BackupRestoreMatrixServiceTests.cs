using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class BackupRestoreMatrixServiceTests
{
    [Fact]
    public void DefinesTheLegacyFiveCellMatrixInStableOrder()
    {
        var alternateRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "backup matrix"));

        var variants = BackupRestoreMatrixService.CreateVariants(alternateRoot);

        Assert.Equal(["baseline", "compound", "source-nondurable", "combined", "alternate-temp"],
            variants.Select(static variant => variant.Name));
        Assert.Equal((false, true, (string?)null),
            (variants[0].Compound, variants[0].SourceDurable, variants[0].TempRoot));
        Assert.Equal((true, true, (string?)null),
            (variants[1].Compound, variants[1].SourceDurable, variants[1].TempRoot));
        Assert.Equal((false, false, (string?)null),
            (variants[2].Compound, variants[2].SourceDurable, variants[2].TempRoot));
        Assert.Equal((true, false, (string?)null),
            (variants[3].Compound, variants[3].SourceDurable, variants[3].TempRoot));
        Assert.Equal((false, true, alternateRoot),
            (variants[4].Compound, variants[4].SourceDurable, variants[4].TempRoot));
    }

    [Fact]
    public void RecordsOverrideNamesButDoesNotRequireAnIterationsOverride()
    {
        var defaultNames = BackupRestoreMatrixService.GetEnvironmentOverrideNames(includeIterations: false);
        var configuredNames = BackupRestoreMatrixService.GetEnvironmentOverrideNames(includeIterations: true);

        Assert.DoesNotContain("CHAOS_ITERATIONS", defaultNames);
        Assert.Contains("CHAOS_ITERATIONS", configuredNames);
        Assert.All(configuredNames, static name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    [Theory]
    [InlineData("net8.0", 1, null)]
    [InlineData("net10.0", 0, null)]
    [InlineData("net11.0", 1, 0)]
    [InlineData("net10.0", int.MaxValue, null)]
    public void RejectsInvalidMatrixOptionsBeforeCreatingArtifacts(string framework, int count, int? iterations)
    {
        var error = BackupRestoreMatrixService.ValidateOptions(new BackupRestoreMatrixOptions
        {
            Framework = framework,
            Count = count,
            Iterations = iterations
        });

        Assert.NotNull(error);
    }

    [Fact(Explicit = true)]
    public async Task RunsManagedFiveCellMatrixAndRetainsLinkedRunEvidence()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var processRunner = new ProcessRunner();
        var service = new BackupRestoreMatrixService(configuration,
            new ArtifactPaths(checkout.RepositoryRoot), processRunner, new DotNetSdkResolver(processRunner),
            new GitRepositoryProvenanceReader(processRunner));
        var result = await service.RunAsync(checkout, new BackupRestoreMatrixOptions
        {
            Framework = "net10.0",
            Count = 1,
            Iterations = 2,
            CommandLine = "devops test backup-restore-matrix -Framework net10.0 -Count 1 -Iterations 2"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        var manifest = Assert.IsType<RunManifest>(result.Manifest);
        Assert.Equal(RunState.Succeeded, manifest.State);
        Assert.Equal(5, result.CompletedCells);
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "source-manifest.json")));
        using var matrix = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.RunDirectory, "matrix.json"),
            TestContext.Current.CancellationToken));
        var cells = matrix.RootElement.GetProperty("cells").EnumerateArray().ToArray();
        Assert.Equal(5, cells.Length);
        Assert.All(cells, cell => Assert.True(File.Exists(Path.Combine(result.RunDirectory, cell.GetProperty("measurementPath").GetString()!))));
        var childIds = cells.Select(cell => Guid.Parse(cell.GetProperty("testRunId").GetString()!)).ToArray();
        Assert.Equal(childIds.Length, childIds.Distinct().Count());
        foreach (var childId in childIds)
        {
            var childPath = new ArtifactPaths(checkout.RepositoryRoot).GetRunDirectory("test", childId);
            using var childManifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(childPath, "run.json"),
                TestContext.Current.CancellationToken));
            Assert.Equal(manifest.RunId.ToString("D"), childManifest.RootElement.GetProperty("parentRunId").GetString());
        }
    }
}

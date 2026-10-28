using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
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

    [Fact]
    public async Task FailedChildExecutionLeavesFailedMatrixEvidenceAndScopesEnvironmentToChild()
    {
        using var checkout = MatrixCheckout.Create();
        var commit = new string('a', 40);
        var sourceManifestPath = Path.Combine(checkout.Root, "source-manifest.json");
        var projectHash = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(await File.ReadAllTextAsync(checkout.ProjectPath, TestContext.Current.CancellationToken))));
        await File.WriteAllTextAsync(sourceManifestPath, JsonSerializer.Serialize(new
        {
            commit,
            files = new[] { new { path = Path.GetRelativePath(checkout.Root, checkout.ProjectPath).Replace('\\', '/'), sha256 = projectHash } }
        }), TestContext.Current.CancellationToken);

        var suites = ImmutableDictionary.Create<string, TestSuiteConfiguration>(StringComparer.OrdinalIgnoreCase)
            .Add("core", new TestSuiteConfiguration("Core", "src/tests/Fake.Core.Tests.csproj", "Mtp",
                ["net10.0"], "net10.0", false, ["Trx"], null, null, null));
        var configuration = new DevOpsConfiguration(suites,
            ImmutableDictionary.Create<string, CodeAreaConfiguration>(StringComparer.OrdinalIgnoreCase),
            ImmutableDictionary.Create<string, BenchmarkProjectConfiguration>(StringComparer.OrdinalIgnoreCase),
            ImmutableDictionary.Create<string, BenchmarkSuiteConfiguration>(StringComparer.OrdinalIgnoreCase),
            ImmutableDictionary.Create<string, BenchmarkStrategyConfiguration>(StringComparer.OrdinalIgnoreCase),
            ImmutableDictionary.Create<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase),
            ImmutableDictionary.Create<string, BenchmarkGroupConfiguration>(StringComparer.OrdinalIgnoreCase));
        var processRunner = new FailingMatrixChildProcessRunner(checkout.Root);
        var artifactPaths = new ArtifactPaths(checkout.Root);
        var service = new BackupRestoreMatrixService(configuration, artifactPaths, processRunner,
            new FixedSdkResolver(), new FixedProvenanceReader(commit));
        var environmentNames = BackupRestoreMatrixService.GetEnvironmentOverrideNames(includeIterations: true);
        var environmentBefore = environmentNames.ToDictionary(static name => name,
            static name => Environment.GetEnvironmentVariable(name), StringComparer.Ordinal);

        var result = await service.RunAsync(checkout.Identity, new BackupRestoreMatrixOptions
        {
            Framework = "net10.0",
            Count = 1,
            Iterations = 2,
            NoRestore = true,
            SourceManifestPath = sourceManifestPath
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.InfrastructureFailure, result.CommandResult.Outcome);
        Assert.Equal(0, result.CompletedCells);
        var manifest = Assert.IsType<RunManifest>(result.Manifest);
        Assert.Equal(RunState.Failed, manifest.State);
        Assert.Contains("baseline-1", manifest.Failure, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "source-manifest.json")));
        using var matrix = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.RunDirectory, "matrix.json"),
            TestContext.Current.CancellationToken));
        Assert.Equal("failed", matrix.RootElement.GetProperty("state").GetString());
        Assert.Equal(0, matrix.RootElement.GetProperty("completedCells").GetInt32());
        Assert.Equal(5, matrix.RootElement.GetProperty("expectedCells").GetInt32());
        Assert.Empty(matrix.RootElement.GetProperty("cells").EnumerateArray());

        var child = Assert.Single(processRunner.ChildRequests);
        var childEnvironment = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string?>>(child.EnvironmentVariables);
        Assert.Equal("false", childEnvironment["LEANCORPUS_BACKUP_COMPOUND"]);
        Assert.Equal("true", childEnvironment["LEANCORPUS_BACKUP_SOURCE_DURABLE"]);
        Assert.Equal("2", childEnvironment["CHAOS_ITERATIONS"]);
        Assert.Equal(Path.Combine(result.RunDirectory, "baseline-1.json"),
            childEnvironment["LEANCORPUS_BACKUP_RESTORE_MEASUREMENT"]);

        var childRunId = Guid.Parse(childEnvironment["LEANCORPUS_RUN_ID"]!);
        using var childManifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(artifactPaths.GetRunDirectory("test", childRunId), "run.json"), TestContext.Current.CancellationToken));
        Assert.Equal("failed", childManifest.RootElement.GetProperty("state").GetString());
        foreach (var name in environmentNames)
        {
            Assert.Equal(environmentBefore[name], Environment.GetEnvironmentVariable(name));
        }
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

    private sealed class MatrixCheckout : IDisposable
    {
        private MatrixCheckout(string root)
        {
            Root = root;
            ProjectPath = Path.Combine(root, "src", "tests", "Fake.Core.Tests.csproj");
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(Path.GetDirectoryName(ProjectPath)!);
            File.WriteAllText(ProjectPath, "<Project />");
            Identity = new CheckoutIdentity(root, Path.Combine(root, ".git"), CheckoutGitMarkerKind.Directory);
        }

        public string Root { get; }
        public string ProjectPath { get; }
        public CheckoutIdentity Identity { get; }

        public static MatrixCheckout Create()
            => new(Path.Combine(Path.GetTempPath(), "LeanCorpus failed matrix " + Guid.NewGuid().ToString("N")));

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class FixedSdkResolver : IDotNetSdkResolver
    {
        public Task<SdkResolution> ResolveAsync(CheckoutIdentity checkout, string runDirectory,
            CancellationToken cancellationToken)
            => Task.FromResult(new SdkResolution(OutcomeKind.Success, "10.0.100", "10.0.100", "latestPatch", null));
    }

    private sealed class FixedProvenanceReader(string commit) : IRepositoryProvenanceReader
    {
        public Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory,
            CancellationToken cancellationToken)
            => Task.FromResult(new GitProvenance(OutcomeKind.Success, commit, "issue-97", false, null));
    }

    private sealed class FailingMatrixChildProcessRunner(string repositoryRoot) : IProcessRunner
    {
        private const string ProjectName = "Fake.Core.Tests";
        private readonly string _targetPath = Path.Combine(repositoryRoot, "artifacts", "bin", ProjectName,
            "release", "net10.0", ProjectName + ".dll");

        public List<ProcessRequest> ChildRequests { get; } = [];

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.StandardOutputLogPath)!);
            await File.WriteAllTextAsync(request.StandardErrorLogPath, string.Empty, cancellationToken);
            if (request.ExecutablePath.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Arguments.Contains("-getProperty:TargetPath", StringComparer.Ordinal))
                {
                    var outputDirectory = Path.GetDirectoryName(_targetPath)!;
                    Directory.CreateDirectory(outputDirectory);
                    await File.WriteAllTextAsync(_targetPath, "fake test assembly", cancellationToken);
                    var appHostName = ProjectName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
                    await File.WriteAllTextAsync(Path.Combine(outputDirectory, appHostName), "fake test apphost", cancellationToken);
                    await File.WriteAllTextAsync(request.StandardOutputLogPath, _targetPath, cancellationToken);
                    return Result(request, OutcomeKind.Success, 0, [_targetPath]);
                }

                await File.WriteAllTextAsync(request.StandardOutputLogPath, string.Empty, cancellationToken);
                return Result(request, OutcomeKind.Success, 0, []);
            }

            ChildRequests.Add(request);
            await File.WriteAllTextAsync(request.StandardOutputLogPath, "simulated child test failure", cancellationToken);
            return Result(request, OutcomeKind.CommandFailure, 1, ["simulated child test failure"]);
        }

        private static ProcessResult Result(ProcessRequest request, OutcomeKind outcome, int exitCode,
            IReadOnlyList<string> standardOutput)
            => new(outcome, exitCode, standardOutput, [], standardOutput.Count, 0, TimeSpan.FromMilliseconds(1),
                request.StandardOutputLogPath, request.StandardErrorLogPath,
                outcome == OutcomeKind.Success ? null : "simulated child test failure");
    }
}

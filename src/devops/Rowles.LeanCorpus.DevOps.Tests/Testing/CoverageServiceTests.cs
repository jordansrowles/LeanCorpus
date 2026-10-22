using System.Collections.Immutable;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class CoverageServiceTests
{
    [Theory]
    [InlineData(false, "Coverage!=Skip")]
    [InlineData(true, "")]
    public async Task UsesRegisteredCoverageSuitesAndPreservesPerformanceExclusion(bool includePerformance, string expectedFilter)
    {
        using var checkout = new TemporaryCheckout();
        var executor = new RecordingTestExecutor(checkout.Root);
        var service = CreateService(checkout, executor, new RecordingProcessRunner());

        var result = await service.RunAsync(Request(checkout, suite: "all", includePerformance: includePerformance),
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.Equal(2, result.CoverageFileCount);
        Assert.NotNull(result.Manifest);
        Assert.Equal(RunState.Succeeded, result.Manifest.State);
        Assert.Equal(result.Manifest.RunId, executor.Request!.ParentRunId);
        Assert.Equal(new[] { "core/net10.0", "sourcegen/net10.0" }, executor.Request.Targets.Select(target => target.Key));
        Assert.All(executor.Request.Targets, target =>
        {
            Assert.True(target.CoverageEligible);
            Assert.Equal(expectedFilter, target.Filter);
        });

        var copiedReports = Directory.GetFiles(Path.Combine(result.RunDirectory!, "raw"), "*.coverage.cobertura.*.xml", SearchOption.AllDirectories);
        Assert.Equal(2, copiedReports.Length);
        Assert.Equal("00000000-0000-0000-0000-000000000001", result.Manifest.Inputs["testRunId"]);
    }

    [Fact]
    public async Task GenerateReportUsesPinnedLocalToolAndRetainsHtmlOutput()
    {
        using var checkout = new TemporaryCheckout();
        checkout.WriteToolManifest();
        var executor = new RecordingTestExecutor(checkout.Root);
        var process = new RecordingProcessRunner { CreateHtml = true };
        var service = CreateService(checkout, executor, process);

        var result = await service.RunAsync(Request(checkout, suite: "core", generateReport: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.Equal(RunState.Succeeded, result.Manifest!.State);
        Assert.Equal("5.5.11", result.Manifest.ToolVersions["ReportGenerator"]);
        Assert.Contains("dotnet", process.Request!.ExecutablePath, StringComparison.Ordinal);
        Assert.Equal(new[] { "tool", "run", "reportgenerator", "--" }, process.Request.Arguments.Take(4));
        Assert.Contains("-reporttypes:Html", process.Request.Arguments);
        Assert.Contains("-assemblyfilters:+Rowles.*;+leancorpus-cli", process.Request.Arguments);
        Assert.Contains("-filefilters:-**/obj/**/*.cs", process.Request.Arguments);
        Assert.True(File.Exists(Path.Combine(result.RunDirectory!, "html", "index.html")));
        Assert.Contains("html", result.Manifest.Artifacts);
    }

    [Fact]
    public async Task MissingPinnedReportGeneratorFailsWithoutUsingAnUnpinnedCommand()
    {
        using var checkout = new TemporaryCheckout();
        var executor = new RecordingTestExecutor(checkout.Root);
        var process = new RecordingProcessRunner();
        var service = CreateService(checkout, executor, process);

        var result = await service.RunAsync(Request(checkout, suite: "core", generateReport: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.InfrastructureFailure, result.CommandResult.Outcome);
        Assert.Equal(RunState.Failed, result.Manifest!.State);
        Assert.Null(process.Request);
        Assert.Contains("tool manifest", result.CommandResult.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ReportGenerator", result.CommandResult.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"version":1,"isRoot":true,"tools":[]}""")]
    public async Task InvalidPinnedToolManifestFailsTheRunWithoutAnUnpinnedInvocation(string manifest)
    {
        using var checkout = new TemporaryCheckout();
        checkout.WriteToolManifest(manifest);
        var executor = new RecordingTestExecutor(checkout.Root);
        var process = new RecordingProcessRunner();
        var service = CreateService(checkout, executor, process);

        var result = await service.RunAsync(Request(checkout, suite: "core", generateReport: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.InfrastructureFailure, result.CommandResult.Outcome);
        Assert.Equal(RunState.Failed, result.Manifest!.State);
        Assert.Null(process.Request);
        Assert.Contains("manifest", result.CommandResult.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReportGeneratorSuccessWithoutAnIndexFailsTheCoverageRun()
    {
        using var checkout = new TemporaryCheckout();
        checkout.WriteToolManifest();
        var executor = new RecordingTestExecutor(checkout.Root);
        var process = new RecordingProcessRunner();
        var service = CreateService(checkout, executor, process);

        var result = await service.RunAsync(Request(checkout, suite: "core", generateReport: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.InfrastructureFailure, result.CommandResult.Outcome);
        Assert.Equal(RunState.Failed, result.Manifest!.State);
        Assert.DoesNotContain("html", result.Manifest.Artifacts);
        Assert.Contains("did not create html/index.html", result.CommandResult.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulTestsWithoutCoberturaReportsFailTheCoverageRun()
    {
        using var checkout = new TemporaryCheckout();
        var executor = new RecordingTestExecutor(checkout.Root) { WriteCoverage = false };
        var service = CreateService(checkout, executor, new RecordingProcessRunner());

        var result = await service.RunAsync(Request(checkout, suite: "core"), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.InfrastructureFailure, result.CommandResult.Outcome);
        Assert.Equal(RunState.Failed, result.Manifest!.State);
        Assert.Contains("without producing Cobertura", result.CommandResult.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CleanRemovesOnlyPriorCoverageOutput()
    {
        using var checkout = new TemporaryCheckout();
        var oldCoverage = Path.Combine(checkout.Root, "artifacts", "coverage", "old-run", "report.html");
        var testOutput = Path.Combine(checkout.Root, "artifacts", "test", "retained.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(oldCoverage)!);
        Directory.CreateDirectory(Path.GetDirectoryName(testOutput)!);
        File.WriteAllText(oldCoverage, "old report");
        File.WriteAllText(testOutput, "test output");

        var service = CreateService(checkout, new RecordingTestExecutor(checkout.Root), new RecordingProcessRunner());
        var result = await service.RunAsync(Request(checkout, suite: "core", clean: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.False(File.Exists(oldCoverage));
        Assert.True(File.Exists(testOutput));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory!, "run.json")));
    }

    [Fact]
    public async Task ReportGeneratorFailureLeavesCoverageRunFailed()
    {
        using var checkout = new TemporaryCheckout();
        checkout.WriteToolManifest();
        var executor = new RecordingTestExecutor(checkout.Root);
        var process = new RecordingProcessRunner { Outcome = OutcomeKind.CommandFailure, ExitCode = 5 };
        var service = CreateService(checkout, executor, process);

        var result = await service.RunAsync(Request(checkout, suite: "core", generateReport: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.CommandFailure, result.CommandResult.Outcome);
        Assert.Equal(RunState.Failed, result.Manifest!.State);
        Assert.Equal(5, Assert.Single(result.Manifest.ChildExitCodes));
        Assert.DoesNotContain("html", result.Manifest.Artifacts);
    }

    private static CoverageService CreateService(
        TemporaryCheckout checkout,
        IManagedTestExecutor executor,
        IProcessRunner processes)
    {
        var paths = new ArtifactPaths(checkout.Root);
        var configuration = CreateConfiguration();
        return new CoverageService(paths,
            new ArtifactRunStore(paths, runCategory: "coverage"),
            new CleanService(paths, new ArtifactRunStore(paths)),
            new TestRunPlanner(configuration, new AffectedSelectionService(new RejectProcessRunner())),
            executor,
            processes);
    }

    private static CoverageRequest Request(
        TemporaryCheckout checkout,
        string suite,
        bool clean = false,
        bool includePerformance = false,
        bool generateReport = false)
        => new(checkout.Identity, suite, "net10.0", "Release", clean, includePerformance, generateReport,
            "./devops coverage");

    private static DevOpsConfiguration CreateConfiguration()
    {
        var suites = ImmutableDictionary.Create<string, TestSuiteConfiguration>(StringComparer.OrdinalIgnoreCase)
            .Add("core", Suite("Core", true))
            .Add("text", Suite("Text", false))
            .Add("sourcegen", Suite("SourceGen", true));
        return new DevOpsConfiguration(
            suites,
            ImmutableDictionary.Create<string, CodeAreaConfiguration>(StringComparer.OrdinalIgnoreCase),
            ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase),
            ImmutableDictionary.Create<string, BenchmarkStrategyConfiguration>(StringComparer.OrdinalIgnoreCase),
            ImmutableDictionary.Create<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase),
            ImmutableDictionary.Create<string, BenchmarkGroupConfiguration>(StringComparer.OrdinalIgnoreCase));
    }

    private static TestSuiteConfiguration Suite(string name, bool coverage)
        => new(name, $"tests/{name}.csproj", "Mtp", ["net10.0", "net11.0"], null, coverage,
            ["Trx"], null, null, null);

    private sealed class RecordingTestExecutor(string repositoryRoot) : IManagedTestExecutor
    {
        public TestExecutorRequest? Request { get; private set; }
        public bool WriteCoverage { get; init; } = true;

        public Task<TestExecutorResult> RunAsync(TestExecutorRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            var runId = Guid.Parse("00000000-0000-0000-0000-000000000001");
            var runDirectory = Path.Combine(repositoryRoot, "artifacts", "test", "runs", runId.ToString("D"));
            Directory.CreateDirectory(runDirectory);
            if (WriteCoverage)
            {
                foreach (var target in request.Targets)
                {
                    var targetDirectory = Path.Combine(runDirectory, target.Key.Replace('/', Path.DirectorySeparatorChar), "1");
                    Directory.CreateDirectory(targetDirectory);
                    File.WriteAllText(Path.Combine(targetDirectory,
                        $"{target.Suite}-{target.Framework}.coverage.cobertura.1.xml"), "<coverage />");
                }
            }

            var manifest = new RunManifest
            {
                SchemaVersion = RunManifest.CurrentSchemaVersion,
                RunId = runId,
                ParentRunId = request.ParentRunId,
                Command = "test",
                State = RunState.Succeeded,
                StartedUtc = DateTimeOffset.UtcNow,
                CompletedUtc = DateTimeOffset.UtcNow,
                SourceCommit = new string('a', 40),
                SourceBranch = "issue-97",
                SourceDirty = false,
                Platform = "test",
                Inputs = new Dictionary<string, string>(),
                ChildExitCodes = [],
                Artifacts = []
            };
            return Task.FromResult(new TestExecutorResult(
                new CommandResult(OutcomeKind.Success, "Tests passed."), runDirectory, null, manifest));
        }
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public ProcessRequest? Request { get; private set; }
        public OutcomeKind Outcome { get; init; } = OutcomeKind.Success;
        public int ExitCode { get; init; }
        public bool CreateHtml { get; init; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            if (Outcome == OutcomeKind.Success && CreateHtml)
            {
                var target = request.Arguments.Single(argument => argument.StartsWith("-targetdir:", StringComparison.Ordinal))[11..];
                Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "index.html"), "coverage report");
            }
            return Task.FromResult(new ProcessResult(Outcome, ExitCode, [], [], 0, 0, TimeSpan.Zero,
                request.StandardOutputLogPath, request.StandardErrorLogPath,
                Outcome == OutcomeKind.Success ? null : "ReportGenerator failed."));
        }
    }

    private sealed class RejectProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Coverage suite planning should not launch a child process.");
    }

    private sealed class TemporaryCheckout : IDisposable
    {
        public TemporaryCheckout()
        {
            Root = Path.Combine(Path.GetTempPath(), $"leancorpus-coverage-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Path.Combine(Root, ".git"));
        }

        public string Root { get; }
        public CheckoutIdentity Identity => new(Root, Path.Combine(Root, ".git"), CheckoutGitMarkerKind.Directory);

        public void WriteToolManifest(string contents = """
            {"version":1,"isRoot":true,"tools":{"dotnet-reportgenerator-globaltool":{"version":"5.5.11","commands":["reportgenerator"]}}}
            """)
        {
            var config = Path.Combine(Root, ".config");
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "dotnet-tools.json"), contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

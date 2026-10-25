using System.Collections.Immutable;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Data;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Data;

public sealed class DataForgeQualificationStepRunnerTests
{
    [Fact]
    public async Task MtpTestUsesTheRuntimeRootOfTheResolvedSdk()
    {
        using var checkout = new TemporaryCheckout();
        var sdkRoot = Path.Combine(checkout.Root, "managed-dotnet");
        var sdkDirectory = Path.Combine(sdkRoot, "sdk", "11.0.100");
        var projectPath = Path.Combine(checkout.Root, "src", "devops", "dataforge", "Rowles.DataForge.Tests", "Rowles.DataForge.Tests.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, "<Project />");
        var targetDirectory = Path.Combine(checkout.Root, "artifacts", "bin", "Rowles.DataForge.Tests", "release_net11.0");
        Directory.CreateDirectory(targetDirectory);
        var targetPath = Path.Combine(targetDirectory, "Rowles.DataForge.Tests.dll");
        File.WriteAllText(targetPath, string.Empty);
        File.WriteAllText(Path.Combine(targetDirectory, "Rowles.DataForge.Tests" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty)), string.Empty);

        var processRunner = new RecordingProcessRunner(targetPath);
        var runner = new DataForgeQualificationStepRunner(
            new ArtifactPaths(checkout.Root),
            processRunner,
            new RecordingSdkResolver(sdkDirectory),
            new RecordingProvenanceReader(),
            TestSuites(dataForgeQualification: true));
        var step = new DataForgeQualificationStep("synthetic-tests", "DataForge net11.0", "./devops test",
            DataForgeQualificationStepKind.Test, ["test"], "dataforge", "net11.0");

        var result = await runner.ExecuteAsync(checkout.Identity, step, Path.Combine(checkout.Root, "qualification"),
            TestContext.Current.CancellationToken);

        Assert.Equal("Passed", result.Status);
        var testRequest = Assert.Single(processRunner.Requests, request => request.Arguments.Contains("--report-xunit-trx"));
        var configIndex = Array.IndexOf(testRequest.Arguments.ToArray(), "--xunit-config-filename");
        Assert.True(configIndex >= 0);
        Assert.Equal(Path.Combine(checkout.Root, ".config", "devops", "testconfig.json"), testRequest.Arguments[configIndex + 1]);
        Assert.Equal(sdkRoot, testRequest.EnvironmentVariables!["DOTNET_ROOT"]);
        var architectureVariable = $"DOTNET_ROOT_{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant()}";
        Assert.Equal(sdkRoot, testRequest.EnvironmentVariables[architectureVariable]);
    }

    [Fact]
    public async Task RejectsConfiguredTestSuiteThatIsNotDataForgeQualificationEligible()
    {
        using var checkout = new TemporaryCheckout();
        var runner = new DataForgeQualificationStepRunner(
            new ArtifactPaths(checkout.Root),
            new RecordingProcessRunner(string.Empty),
            new RecordingSdkResolver(string.Empty),
            new RecordingProvenanceReader(),
            TestSuites(dataForgeQualification: false));
        var step = new DataForgeQualificationStep("synthetic-tests", "DataForge net11.0", "./devops test",
            DataForgeQualificationStepKind.Test, ["test"], "dataforge", "net11.0");

        var result = await runner.ExecuteAsync(checkout.Identity, step, Path.Combine(checkout.Root, "qualification"),
            TestContext.Current.CancellationToken);

        Assert.Equal("Failed", result.Status);
        Assert.Equal("Configured test suite 'dataforge' is not eligible for DataForge qualification.", result.Error);
    }

    private static IReadOnlyDictionary<string, TestSuiteConfiguration> TestSuites(bool dataForgeQualification)
        => ImmutableDictionary.Create<string, TestSuiteConfiguration>(StringComparer.OrdinalIgnoreCase).Add(
            "dataforge",
            new TestSuiteConfiguration("DataForge", "src/devops/dataforge/Rowles.DataForge.Tests/Rowles.DataForge.Tests.csproj", "Mtp",
                ["net10.0", "net11.0"], "net11.0", false, ["Trx"], null, null, null,
                DataForgeQualification: dataForgeQualification));

    private sealed class RecordingProcessRunner(string targetPath) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Directory.CreateDirectory(Path.GetDirectoryName(request.StandardOutputLogPath)!);
            File.WriteAllText(request.StandardOutputLogPath, string.Empty);
            File.WriteAllText(request.StandardErrorLogPath, string.Empty);
            if (request.Arguments.FirstOrDefault() == "msbuild")
            {
                return Task.FromResult(new ProcessResult(OutcomeKind.Success, 0, [targetPath], [], 1, 0,
                    TimeSpan.Zero, request.StandardOutputLogPath, request.StandardErrorLogPath, null));
            }

            var resultsDirectory = request.Arguments[Array.IndexOf(request.Arguments.ToArray(), "--results-directory") + 1];
            var fileName = request.Arguments[Array.IndexOf(request.Arguments.ToArray(), "--report-xunit-trx-filename") + 1];
            File.WriteAllText(Path.Combine(resultsDirectory, fileName),
                "<TestRun><ResultSummary outcome=\"Completed\"><Counters total=\"1\" executed=\"1\" passed=\"1\" failed=\"0\" error=\"0\" timeout=\"0\" aborted=\"0\" inconclusive=\"0\" notExecuted=\"0\" disconnected=\"0\" /></ResultSummary></TestRun>");
            return Task.FromResult(new ProcessResult(OutcomeKind.Success, 0, [], [], 0, 0, TimeSpan.Zero,
                request.StandardOutputLogPath, request.StandardErrorLogPath, null));
        }
    }

    private sealed class RecordingSdkResolver(string sdkDirectory) : IDotNetSdkResolver
    {
        public Task<SdkResolution> ResolveAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new SdkResolution(OutcomeKind.Success, "11.0.100", "11.0.100", "latestPatch", null, sdkDirectory));
    }

    private sealed class RecordingProvenanceReader : IRepositoryProvenanceReader
    {
        public Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new GitProvenance(OutcomeKind.Success, "commit", "branch", false, null));
    }

    private sealed class TemporaryCheckout : IDisposable
    {
        public TemporaryCheckout()
        {
            Root = Path.Combine(Path.GetTempPath(), "LeanCorpus managed MTP runtime " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            var gitPath = Path.Combine(Root, ".git");
            Directory.CreateDirectory(gitPath);
            Identity = new CheckoutIdentity(Root, gitPath, CheckoutGitMarkerKind.Directory);
        }

        public string Root { get; }

        public CheckoutIdentity Identity { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

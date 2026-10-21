using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Build;

public sealed class BuildServiceTests
{
    [Fact]
    public async Task SolutionBuildKeepsDeclaredFrameworksAndLegacyLoggingFlags()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new RecordingProcessRunner();
        var service = CreateService(checkout, process);

        var result = await service.BuildAsync(Request(checkout), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.NotNull(result.Manifest);
        Assert.Equal(RunState.Succeeded, result.Manifest.State);
        var args = process.Request!.Arguments;
        Assert.Equal("build", args[0]);
        Assert.Equal(Path.Combine(checkout.Root, "Rowles.LeanCorpus.slnx"), args[1]);
        Assert.DoesNotContain("-f", args);
        Assert.Contains("--disable-build-servers", args);
        Assert.Contains("-m:1", args);
        Assert.Contains("-p:UseSharedCompilation=false", args);
        Assert.Contains("--tl:off", args);
        Assert.Contains(args, argument => argument.StartsWith("-bl:", StringComparison.Ordinal));
        Assert.Contains(args, argument => argument.StartsWith("-flp:logfile=", StringComparison.Ordinal) && argument.EndsWith(";verbosity=normal", StringComparison.Ordinal));
        Assert.Equal("abc123", result.Manifest.SourceCommit);
        Assert.Equal("main", result.Manifest.SourceBranch);
        Assert.True(result.Manifest.SourceDirty);
        Assert.Equal("11.0.100-preview.7.26381.103", result.Manifest.ToolVersions["dotnet-sdk-selected"]);
    }

    [Fact]
    public async Task ProjectBuildDefaultsToNet11AndKeepsPathWithSpacesAsOneToken()
    {
        using var checkout = TemporaryCheckout.Create("DevOps build checkout ");
        var process = new RecordingProcessRunner();
        var service = CreateService(checkout, process);
        var request = Request(checkout) with { ProjectPath = "src/Example Project/Example.csproj" };

        var result = await service.BuildAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        var args = process.Request!.Arguments;
        Assert.Equal(Path.Combine(checkout.Root, "src", "Example Project", "Example.csproj"), args[1]);
        Assert.Equal("-f", args[4]);
        Assert.Equal("net11.0", args[5]);
        Assert.DoesNotContain("--no-restore", args);
    }

    [Fact]
    public async Task ProjectBuildPassesExplicitFrameworkRidAndRestorePolicyAsSeparateTokens()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new RecordingProcessRunner();
        var service = CreateService(checkout, process);
        var request = Request(checkout) with
        {
            ProjectPath = "src/Example/Example.csproj",
            Configuration = "Debug",
            Framework = "net10.0",
            RuntimeIdentifier = "linux-x64",
            NoRestore = true
        };

        var result = await service.BuildAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.Equal(["-f", "net10.0"], result.Arguments.Skip(4).Take(2));
        Assert.Contains("-r", result.Arguments);
        Assert.Contains("linux-x64", result.Arguments);
        Assert.Contains("--no-restore", result.Arguments);
    }

    [Fact]
    public async Task RejectsFrameworkForSolutionAndProjectEscapeBeforeCreatingRun()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new RecordingProcessRunner();
        var service = CreateService(checkout, process);
        var invalidSolution = await service.BuildAsync(Request(checkout) with { Framework = "net10.0" }, TestContext.Current.CancellationToken);
        var invalidProject = await service.BuildAsync(Request(checkout) with { ProjectPath = "../outside.csproj" }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.ValidationFailure, invalidSolution.CommandResult.Outcome);
        Assert.Equal(OutcomeKind.ValidationFailure, invalidProject.CommandResult.Outcome);
        Assert.Null(invalidSolution.RunId);
        Assert.Null(invalidProject.RunId);
        Assert.Null(process.Request);
        Assert.False(Directory.Exists(Path.Combine(checkout.Root, "artifacts", "build", "runs")));
    }

    [Fact]
    public async Task ChildFailureRetainsRawExitAndFinalisesFailedRun()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new RecordingProcessRunner(exitCode: 23);
        var service = CreateService(checkout, process);

        var result = await service.BuildAsync(Request(checkout), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.CommandFailure, result.CommandResult.Outcome);
        Assert.Equal(23, result.CommandResult.RawChildExitCode);
        Assert.Equal(RunState.Failed, result.Manifest!.State);
        Assert.Equal([23], result.Manifest.ChildExitCodes);
    }

    private static BuildService CreateService(TemporaryCheckout checkout, RecordingProcessRunner process)
        => new(
            new ArtifactPaths(checkout.Root),
            new ArtifactRunStore(new ArtifactPaths(checkout.Root)),
            process,
            new FakeSdkResolver(),
            new FakeProvenanceReader());

    private static BuildRequest Request(TemporaryCheckout checkout)
        => new()
        {
            Checkout = checkout.Identity,
            Configuration = "Release"
        };

    private sealed class RecordingProcessRunner(int exitCode = 0) : IProcessRunner
    {
        public ProcessRequest? Request { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new ProcessResult(
                exitCode == 0 ? OutcomeKind.Success : OutcomeKind.CommandFailure,
                exitCode,
                ["11.0.100-preview.7.26381.103"],
                [],
                1,
                0,
                TimeSpan.FromMilliseconds(10),
                request.StandardOutputLogPath,
                request.StandardErrorLogPath,
                exitCode == 0 ? null : "fake child failure"));
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
            => Task.FromResult(new GitProvenance(OutcomeKind.Success, "abc123", "main", true, null));
    }

    private sealed class TemporaryCheckout : IDisposable
    {
        private TemporaryCheckout(string root, CheckoutIdentity identity)
        {
            Root = root;
            Identity = identity;
        }

        public string Root { get; }

        public CheckoutIdentity Identity { get; }

        public static TemporaryCheckout Create(string prefix = "DevOps build checkout ")
        {
            var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(Path.Combine(root, "src", "Example Project"));
            Directory.CreateDirectory(Path.Combine(root, "src", "Example"));
            File.WriteAllText(Path.Combine(root, "global.json"), "{\"sdk\":{\"version\":\"11.0.100-preview.7.26381.103\"}}");
            File.WriteAllText(Path.Combine(root, "Rowles.LeanCorpus.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(root, "src", "Example", "Example.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(root, "src", "Example Project", "Example.csproj"), "<Project />");
            return new TemporaryCheckout(root, new CheckoutIdentity(root, Path.Combine(root, ".git"), CheckoutGitMarkerKind.Directory));
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

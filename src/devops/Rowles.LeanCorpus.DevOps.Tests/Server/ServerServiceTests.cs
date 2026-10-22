using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Server;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Server;

public sealed class ServerServiceTests
{
    [Fact]
    public async Task DefaultsToNet11DebugAndPreservesLocalListenerPolicy()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new RecordingProcessRunner();
        var service = CreateService(checkout, process);

        var result = await service.StartAsync(new ServerStartRequest { Checkout = checkout.Identity }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.Equal(["run", "--project", Path.GetFullPath(Path.Combine(checkout.Root, "src", "server", "Rowles.LeanCorpus.Server.Local", "Rowles.LeanCorpus.Server.Local.csproj")), "--framework", "net11.0", "--configuration", "Debug"], process.Request!.Arguments);
        Assert.Equal(checkout.Root, process.Request.WorkingDirectory);
        Assert.Equal(RunState.Succeeded, result.Manifest!.State);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net11.0")]
    public async Task SupportsBothFrameworks(string framework)
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new RecordingProcessRunner();
        var result = await CreateService(checkout, process).StartAsync(
            new ServerStartRequest { Checkout = checkout.Identity, Framework = framework }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.Equal(framework, process.Request!.Arguments[4]);
    }

    [Fact]
    public async Task PassesNoBuildNoRestoreExternalPolicyAndApplicationArgumentsAsTokens()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new RecordingProcessRunner();
        var request = new ServerStartRequest
        {
            Checkout = checkout.Identity,
            Framework = "net10.0",
            Configuration = "Release",
            NoBuild = true,
            NoRestore = true,
            External = true,
            ApplicationArguments = ["--urls=http://127.0.0.1:5081", "--custom", "value with spaces"]
        };

        var result = await CreateService(checkout, process).StartAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.Equal(
        [
            "run", "--project", Path.GetFullPath(Path.Combine(checkout.Root, "src", "server", "Rowles.LeanCorpus.Server.Local", "Rowles.LeanCorpus.Server.Local.csproj")),
            "--framework", "net10.0", "--configuration", "Release", "--no-build", "--no-restore", "--",
            "--urls=http://0.0.0.0:5080", "--AllowedHosts=*", "--urls=http://127.0.0.1:5081", "--custom", "value with spaces"
        ], process.Request!.Arguments);
    }

    [Fact]
    public async Task RejectsInvalidFrameworkAndConfigurationBeforeStartingChild()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new RecordingProcessRunner();
        var service = CreateService(checkout, process);

        var framework = await service.StartAsync(new ServerStartRequest { Checkout = checkout.Identity, Framework = "net12.0" }, TestContext.Current.CancellationToken);
        var configuration = await service.StartAsync(new ServerStartRequest { Checkout = checkout.Identity, Configuration = "Staging" }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.ValidationFailure, framework.CommandResult.Outcome);
        Assert.Equal(OutcomeKind.ValidationFailure, configuration.CommandResult.Outcome);
        Assert.Null(framework.RunId);
        Assert.Null(configuration.RunId);
        Assert.Null(process.Request);
    }

    [Fact]
    public async Task ChildFailureRetainsRawExitAndFinalisesManifest()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new RecordingProcessRunner(exitCode: 23);

        var result = await CreateService(checkout, process).StartAsync(
            new ServerStartRequest { Checkout = checkout.Identity }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.CommandFailure, result.CommandResult.Outcome);
        Assert.Equal(23, result.CommandResult.RawChildExitCode);
        Assert.Equal(RunState.Failed, result.Manifest!.State);
    }

    [Fact]
    public async Task CancellationStopsForegroundChildFinalisesManifestAndReleasesLease()
    {
        using var checkout = TemporaryCheckout.Create();
        using var cancellation = new CancellationTokenSource();
        var process = new BlockingProcessRunner();
        var service = CreateService(checkout, process);
        var operation = service.StartAsync(new ServerStartRequest { Checkout = checkout.Identity }, cancellation.Token);
        await process.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();

        var result = await operation;
        await using var exclusive = await ExecutionLease.AcquireExclusiveAsync(checkout.Root, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Cancelled, result.CommandResult.Outcome);
        Assert.Equal(RunState.Cancelled, result.Manifest!.State);
        Assert.Contains("process.stdout.log", result.Manifest.Artifacts);
        Assert.Contains("process.stderr.log", result.Manifest.Artifacts);
        Assert.True(File.Exists(Path.Combine(result.RunDirectory!, "process.stdout.log")));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory!, "process.stderr.log")));
    }

    private static ServerService CreateService(TemporaryCheckout checkout, IProcessRunner process)
    {
        var paths = new ArtifactPaths(checkout.Root);
        return new ServerService(paths, new ArtifactRunStore(paths), process, new FakeSdkResolver(), new FakeProvenanceReader());
    }

    private sealed class RecordingProcessRunner(int exitCode = 0) : IProcessRunner
    {
        public ProcessRequest? Request { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(Result(request, exitCode == 0 ? OutcomeKind.Success : OutcomeKind.CommandFailure, exitCode));
        }
    }

    private sealed class BlockingProcessRunner : IProcessRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.StandardOutputLogPath)!);
            await File.WriteAllTextAsync(request.StandardOutputLogPath, "child stdout", cancellationToken);
            await File.WriteAllTextAsync(request.StandardErrorLogPath, "child stderr", cancellationToken);
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Result(request, OutcomeKind.Cancelled, null);
            }

            throw new InvalidOperationException("The foreground test process should only stop through cancellation.");
        }
    }

    private static ProcessResult Result(ProcessRequest request, OutcomeKind outcome, int? exitCode)
        => new(outcome, exitCode, [], [], 0, 0, TimeSpan.FromMilliseconds(5), request.StandardOutputLogPath, request.StandardErrorLogPath,
            outcome == OutcomeKind.CommandFailure ? "fake server failure" : null);

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
        private TemporaryCheckout(string root)
        {
            Root = root;
            Identity = new CheckoutIdentity(root, Path.Combine(root, ".git"), CheckoutGitMarkerKind.Directory);
        }

        public string Root { get; }

        public CheckoutIdentity Identity { get; }

        public static TemporaryCheckout Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "DevOps server checkout " + Guid.NewGuid().ToString("N"));
            var projectDirectory = Path.Combine(root, "src", "server", "Rowles.LeanCorpus.Server.Local");
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(Path.Combine(root, "global.json"), "{}");
            File.WriteAllText(Path.Combine(root, "Rowles.LeanCorpus.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(projectDirectory, "Rowles.LeanCorpus.Server.Local.csproj"), "<Project />");
            return new TemporaryCheckout(root);
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

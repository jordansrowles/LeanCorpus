using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Build;

public sealed class DotNetPublishServiceTests
{
    [Fact]
    public async Task PublishUsesTypedTokensAndRecordsOutputWithoutRunningTests()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new RecordingProcessRunner();
        var output = Path.Combine(checkout.Root, "artifacts", "devops", "bootstrap-stage", "linux-x64");
        var request = new DotNetPublishRequest
        {
            Checkout = checkout.Identity,
            ProjectPath = "src/devops/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj",
            Configuration = "Release",
            Framework = "net10.0",
            RuntimeIdentifier = "linux-x64",
            OutputPath = output,
            NoRestore = true,
            SelfContained = false,
            SingleFile = true,
            PublishAot = false,
            PublishTrimmed = false
        };

        var result = await CreateService(checkout, process).PublishAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.Equal(
        [
            "publish", Path.Combine(checkout.Root, "src", "devops", "Rowles.LeanCorpus.DevOps", "Rowles.LeanCorpus.DevOps.csproj"),
            "-c", "Release", "-f", "net10.0", "-r", "linux-x64", "-o", output, "--no-restore", "--self-contained", "false",
            "-p:PublishSingleFile=true", "-p:PublishAot=false", "-p:PublishTrimmed=false", "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false", "--tl:off"
        ], process.Request!.Arguments);
        Assert.Equal("false", result.Manifest!.Inputs["publish-aot"]);
        Assert.Equal(RunState.Succeeded, result.Manifest.State);
    }

    [Fact]
    public async Task PublishUsesLockedRestoreAndNativeLibraryExtractionWhenRequested()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new RecordingProcessRunner();
        var request = Request(checkout) with
        {
            RestoreLockedMode = true,
            IncludeNativeLibrariesForSelfExtract = true,
            SuppressDebugSymbols = true,
            ReleaseArchiveProfile = true
        };

        var result = await CreateService(checkout, process).PublishAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.Contains("-p:RestoreLockedMode=true", process.Request!.Arguments);
        Assert.Contains("-p:IncludeNativeLibrariesForSelfExtract=true", process.Request.Arguments);
        Assert.Contains("-p:DevOpsDistributionProfile=ReleaseArchive", process.Request.Arguments);
        Assert.Contains("-p:DebugType=None", process.Request.Arguments);
        Assert.Contains("-p:DebugSymbols=false", process.Request.Arguments);
        var manifest = result.Manifest!;
        Assert.Equal("true", manifest.Inputs["suppressDebugSymbols"]);
        Assert.Equal("true", manifest.Inputs["restoreLockedMode"]);
        Assert.Equal("true", manifest.Inputs["includeNativeLibrariesForSelfExtract"]);
        Assert.Equal("true", manifest.Inputs["releaseArchiveProfile"]);
    }

    [Fact]
    public async Task RejectsProjectOrOutputEscapeBeforeCreatingRun()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new RecordingProcessRunner();
        var service = CreateService(checkout, process);
        var request = Request(checkout);

        var project = await service.PublishAsync(request with { ProjectPath = "../outside.csproj" }, TestContext.Current.CancellationToken);
        var output = await service.PublishAsync(request with { OutputPath = Path.Combine(Path.GetTempPath(), "outside-publish") }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.ValidationFailure, project.CommandResult.Outcome);
        Assert.Equal(OutcomeKind.ValidationFailure, output.CommandResult.Outcome);
        Assert.Null(project.RunId);
        Assert.Null(output.RunId);
        Assert.Null(process.Request);
    }

    private static DotNetPublishService CreateService(TemporaryCheckout checkout, RecordingProcessRunner process)
    {
        var paths = new ArtifactPaths(checkout.Root);
        return new DotNetPublishService(paths, new ArtifactRunStore(paths), process, new FakeSdkResolver(), new FakeProvenanceReader());
    }

    private static DotNetPublishRequest Request(TemporaryCheckout checkout)
        => new()
        {
            Checkout = checkout.Identity,
            ProjectPath = "src/devops/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj",
            RuntimeIdentifier = "linux-x64",
            OutputPath = Path.Combine(checkout.Root, "artifacts", "publish")
        };

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public ProcessRequest? Request { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new ProcessResult(OutcomeKind.Success, 0, [], [], 0, 0, TimeSpan.FromMilliseconds(5), request.StandardOutputLogPath, request.StandardErrorLogPath, null));
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
        private TemporaryCheckout(string root)
        {
            Root = root;
            Identity = new CheckoutIdentity(root, Path.Combine(root, ".git"), CheckoutGitMarkerKind.Directory);
        }

        public string Root { get; }

        public CheckoutIdentity Identity { get; }

        public static TemporaryCheckout Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "DevOps publish checkout " + Guid.NewGuid().ToString("N"));
            var projectDirectory = Path.Combine(root, "src", "devops", "Rowles.LeanCorpus.DevOps");
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(Path.Combine(root, "global.json"), "{}");
            File.WriteAllText(Path.Combine(root, "Rowles.LeanCorpus.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(projectDirectory, "Rowles.LeanCorpus.DevOps.csproj"), "<Project />");
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

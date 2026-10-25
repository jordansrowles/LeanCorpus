using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Packaging;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Packaging;

public sealed class PackageConsumerValidationServiceTests
{
    [Fact]
    public async Task RejectsCorruptPackageManifestBeforeStartingConsumerProcesses()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new RecordingProcessRunner();
        var paths = new ArtifactPaths(checkout.Root);
        var service = new PackageConsumerValidationService(paths, new ArtifactRunStore(paths, runCategory: "test"), process,
            new FakeProvenanceReader());

        var result = await service.ValidateAsync(new PackageConsumerValidationRequest
        {
            Checkout = checkout.Identity,
            PackageDirectory = checkout.PackageDirectory,
            Scope = "all"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.ValidationFailure, result.CommandResult.Outcome);
        Assert.NotNull(result.Manifest);
        Assert.Equal(RunState.Failed, result.Manifest.State);
        Assert.Empty(process.Requests);
        Assert.True(File.Exists(result.AcceptancePath));
        using var acceptance = JsonDocument.Parse(File.ReadAllText(result.AcceptancePath!));
        Assert.Equal("failed", acceptance.RootElement.GetProperty("status").GetString());
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessResult(OutcomeKind.InfrastructureFailure, null, [], [], 0, 0, TimeSpan.Zero,
                request.StandardOutputLogPath, request.StandardErrorLogPath, "Unexpected child process."));
        }
    }

    private sealed class FakeProvenanceReader : IRepositoryProvenanceReader
    {
        public Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new GitProvenance(OutcomeKind.Success, "abc123", "issue-97", true, null));
    }

    private sealed class TemporaryCheckout : IDisposable
    {
        private TemporaryCheckout(string root)
        {
            Root = root;
            PackageDirectory = Path.Combine(root, "artifacts/package/release");
            Identity = new CheckoutIdentity(root, Path.Combine(root, ".git"), CheckoutGitMarkerKind.Directory);
        }

        public string Root { get; }

        public string PackageDirectory { get; }

        public CheckoutIdentity Identity { get; }

        public static TemporaryCheckout Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "leancorpus-package-consumers-" + Guid.NewGuid().ToString("N"));
            var packageDirectory = Path.Combine(root, "artifacts/package/release");
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(packageDirectory);
            var archiveName = "LeanCorpus.4.0.0.nupkg";
            var archivePath = Path.Combine(packageDirectory, archiveName);
            File.WriteAllText(archivePath, "corrupt package bytes");
            var manifest = new
            {
                schemaVersion = 1,
                sourceCommit = "abc123",
                packages = new[]
                {
                    new { packageId = "LeanCorpus", version = "4.0.0", path = archiveName, size = new FileInfo(archivePath).Length, sha256 = new string('0', 64) }
                }
            };
            File.WriteAllText(Path.Combine(packageDirectory, "manifest.json"), JsonSerializer.Serialize(manifest));
            return new TemporaryCheckout(root);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

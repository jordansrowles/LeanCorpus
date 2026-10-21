using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Data;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;
using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Tests.Data;

public sealed class DataForgeQualificationServiceTests
{
    [Fact]
    public async Task DryRunPrintsTheManagedPlanWithoutCreatingRunsOrResolvingTools()
    {
        using var checkout = new TemporaryCheckout();
        var output = new List<string>();
        var runner = new RecordingStepRunner();
        var provenance = new RecordingProvenanceReader();
        var sdk = new RecordingSdkResolver();
        var paths = new ArtifactPaths(checkout.Identity.RepositoryRoot);
        var service = new DataForgeQualificationService(paths, new AtomicFileWriter(), runner, provenance, sdk, output.Add);

        var result = await service.RunAsync(checkout.Identity, new DataForgeQualificationOptions("/tmp/wikipedia", Dry: true), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.Null(result.QualificationDirectory);
        Assert.Equal(25, output.Count);
        Assert.Contains(output, line => line.Contains("[wikipedia-reference]", StringComparison.Ordinal));
        Assert.Empty(runner.Steps);
        Assert.Equal(0, provenance.Calls);
        Assert.Equal(0, sdk.Calls);
        Assert.False(Directory.Exists(paths.ArtifactRoot));
    }

    [Fact]
    public async Task MissingReferenceIsBlockedAndWikipediaRunsAreSkipped()
    {
        using var checkout = new TemporaryCheckout();
        var paths = new ArtifactPaths(checkout.Identity.RepositoryRoot);
        var runner = new SuccessfulStepRunner();
        var service = new DataForgeQualificationService(paths, new AtomicFileWriter(), runner,
            new RecordingProvenanceReader(), new RecordingSdkResolver());

        var result = await service.RunAsync(checkout.Identity,
            new DataForgeQualificationOptions(Path.Combine(checkout.Identity.RepositoryRoot, "missing-reference"), Dry: false), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.CommandFailure, result.CommandResult.Outcome);
        Assert.NotNull(result.QualificationDirectory);
        using var summary = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(result.QualificationDirectory!, "qualification.json")));
        Assert.Equal("Failed", summary.RootElement.GetProperty("status").GetString());
        Assert.False(summary.RootElement.GetProperty("wikipedia").GetProperty("verified").GetBoolean());
        Assert.DoesNotContain(runner.Executed, step => step.Group.StartsWith("wikipedia-", StringComparison.Ordinal));
        var commands = summary.RootElement.GetProperty("commands").EnumerateArray().ToArray();
        Assert.Contains(commands, command => command.GetProperty("status").GetString() == "Blocked");
        Assert.All(commands.Where(command => command.GetProperty("group").GetString() is "wikipedia-smoke" or "wikipedia-benchmarks"),
            command => Assert.Equal("Skipped", command.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task ExistingReferenceRunsItsVerifierBeforeWikipediaBenchmarks()
    {
        using var checkout = new TemporaryCheckout();
        var referencePath = Path.Combine(checkout.Identity.RepositoryRoot, "reference");
        Directory.CreateDirectory(referencePath);
        File.WriteAllText(Path.Combine(referencePath, "manifest.json"), $$"""
            {
              "sourceKind": "Imported",
              "source": { "datasetId": "leancorpus-wikipedia-en", "datasetVersion": "1", "dumpDate": "20260901", "primarySha1": "primary", "indexSha1": "index" },
              "recordCount": 20000,
              "logicalByteCount": 123,
              "contentSha256": "{{new string('a', 64)}}",
              "artefactSha256": "{{new string('b', 64)}}"
            }
            """);
        var paths = new ArtifactPaths(checkout.Identity.RepositoryRoot);
        var runner = new SuccessfulStepRunner();
        var service = new DataForgeQualificationService(paths, new AtomicFileWriter(), runner,
            new RecordingProvenanceReader(), new RecordingSdkResolver());

        var result = await service.RunAsync(checkout.Identity, new DataForgeQualificationOptions(referencePath, Dry: false),
            TestContext.Current.CancellationToken);

        Assert.Contains(runner.Executed, step => step.Kind == DataForgeQualificationStepKind.ReferenceVerification);
        Assert.Contains(runner.Executed, step => step.Group == "wikipedia-smoke");
        using var summary = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(result.QualificationDirectory!, "qualification.json")));
        Assert.Contains(summary.RootElement.GetProperty("commands").EnumerateArray(),
            command => command.GetProperty("group").GetString() == "wikipedia-reference" &&
                       command.GetProperty("status").GetString() == "Passed");
    }

    private sealed class RecordingStepRunner : IDataForgeQualificationStepRunner
    {
        public List<DataForgeQualificationStep> Steps { get; } = [];

        public Task<DataForgeQualificationStepResult> ExecuteAsync(CheckoutIdentity checkout, DataForgeQualificationStep step,
            string qualificationDirectory, CancellationToken cancellationToken)
        {
            Steps.Add(step);
            return Task.FromResult(new DataForgeQualificationStepResult("Passed", 0, 0));
        }
    }

    private sealed class SuccessfulStepRunner : IDataForgeQualificationStepRunner
    {
        public List<DataForgeQualificationStep> Executed { get; } = [];

        public Task<DataForgeQualificationStepResult> ExecuteAsync(CheckoutIdentity checkout, DataForgeQualificationStep step,
            string qualificationDirectory, CancellationToken cancellationToken)
        {
            Executed.Add(step);
            var kind = step.Kind switch
            {
                DataForgeQualificationStepKind.Build => "build",
                DataForgeQualificationStepKind.ReferenceVerification => "dataforge",
                DataForgeQualificationStepKind.Test => "test",
                DataForgeQualificationStepKind.Benchmark => "benchmark",
                _ => string.Empty
            };
            return Task.FromResult(new DataForgeQualificationStepResult("Passed", 0, 0, Guid.NewGuid(), kind,
                "artifacts/fake-run", qualificationDirectory));
        }
    }

    private sealed class RecordingProvenanceReader : IRepositoryProvenanceReader
    {
        public int Calls { get; private set; }

        public Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new GitProvenance(OutcomeKind.Success, "commit", "branch", false, null));
        }
    }

    private sealed class RecordingSdkResolver : IDotNetSdkResolver
    {
        public int Calls { get; private set; }

        public Task<SdkResolution> ResolveAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new SdkResolution(OutcomeKind.Success, "11.0.0", "11.0.0", "latestPatch", null));
        }
    }

    private sealed class TemporaryCheckout : IDisposable
    {
        public TemporaryCheckout()
        {
            var root = Path.Combine(Path.GetTempPath(), "LeanCorpus qualification " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var gitPath = Path.Combine(root, ".git");
            Directory.CreateDirectory(gitPath);
            Identity = new CheckoutIdentity(root, gitPath, CheckoutGitMarkerKind.Directory);
        }

        public CheckoutIdentity Identity { get; }

        public void Dispose() => Directory.Delete(Identity.RepositoryRoot, recursive: true);
    }
}

using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Data;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Data;

public sealed class DataForgeCommandServiceTests
{
    [Fact]
    public void BuildsAReleaseDotnetRunCommandWithoutChangingToolArgumentBoundaries()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "LeanCorpus checkout");
        string[] toolArguments = ["generate", "-Profile", "leancorpus-search", "-Output", "data set"];

        var arguments = DataForgeCommandService.BuildArguments(repositoryRoot, toolArguments);

        Assert.Equal("run", arguments[0]);
        Assert.Equal("--project", arguments[1]);
        Assert.Equal(Path.Combine(repositoryRoot, "src", "devops", "dataforge", "Rowles.DataForge.Tool", "Rowles.DataForge.Tool.csproj"), arguments[2]);
        var expectedTail = new[] { "--framework", "net10.0", "--configuration", "Release", "--" }.Concat(toolArguments);
        Assert.Equal(expectedTail, arguments.Skip(3));
    }

    [Fact]
    public async Task RunAsyncReportsPrepareResolveAndExecutePhases()
    {
        string root = Path.Combine(Path.GetTempPath(), "leancorpus-dataforge-progress-" + Guid.NewGuid().ToString("N"));
        string project = Path.Combine(root, "src", "devops", "dataforge", "Rowles.DataForge.Tool", "Rowles.DataForge.Tool.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(project, "<Project />");
        var paths = new ArtifactPaths(root);
        var progress = new List<string>();
        var service = new DataForgeCommandService(
            paths,
            new ArtifactRunStore(paths, runCategory: "dataforge"),
            new SuccessfulProcessRunner(),
            new SuccessfulSdkResolver(),
            new SuccessfulProvenanceReader(),
            progress.Add);

        try
        {
            var result = await service.RunAsync(new DataForgeCommandRequest(
                new CheckoutIdentity(root, null, null), ["profiles"]));

            Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
            Assert.Equal(
                ["Preparing DataForge run evidence.",
                 "Recording DataForge source provenance.",
                 "Resolving the SDK selected by global.json.",
                 "Executing the DataForge tool."],
                progress.Take(4));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class SuccessfulProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ProcessResult(OutcomeKind.Success, 0, ["ok"], [], 1, 0, TimeSpan.Zero,
                request.StandardOutputLogPath, request.StandardErrorLogPath, null));
    }

    private sealed class SuccessfulSdkResolver : IDotNetSdkResolver
    {
        public Task<SdkResolution> ResolveAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new SdkResolution(OutcomeKind.Success, "11.0.100-preview.7.26381.103",
                "11.0.100-preview.7.26381.103", "latestPatch", null));
    }

    private sealed class SuccessfulProvenanceReader : IRepositoryProvenanceReader
    {
        public Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new GitProvenance(OutcomeKind.Success, "abc123", "issue-97", true, null));
    }
}

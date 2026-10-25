using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Documentation;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Documentation;

public sealed class DocumentationSiteMetadataWriterTests
{
    [Fact]
    public async Task DisablesMsBuildNodeReuseForVersionProbes()
    {
        using var directory = new TemporaryDirectory();
        var repositoryRoot = directory.Path;
        CreateProject(repositoryRoot, "src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj");
        var processRunner = new VersionProcessRunner();

        await new DocumentationSiteMetadataWriter(processRunner)
            .EvaluateVersionsAsync(repositoryRoot,
                Path.Combine(repositoryRoot, "artifacts", "docs", "runs", "test-run"),
                TestContext.Current.CancellationToken);

        var request = Assert.Single(processRunner.Requests);
        Assert.Contains("-nr:false", request.Arguments);
    }

    [Fact]
    public async Task WritesGeneratedJsonWithTheLegacyFinalNewline()
    {
        using var directory = new TemporaryDirectory();
        var repositoryRoot = directory.Path;
        CreateProject(repositoryRoot, "src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj");
        CreateProject(repositoryRoot, "src/devops/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj");
        var checkout = new CheckoutIdentity(repositoryRoot, Path.Combine(repositoryRoot, ".git"), CheckoutGitMarkerKind.Directory);
        var provenance = new GitProvenance(OutcomeKind.Success, new string('a', 40), "issue-97", true, null);
        var generated = Path.Combine(repositoryRoot, "artifacts", "docs", "generated");
        var runDirectory = Path.Combine(repositoryRoot, "artifacts", "docs", "runs", "test-run");

        var result = await new DocumentationSiteMetadataWriter(new VersionProcessRunner())
            .WriteAsync(checkout, generated, runDirectory, provenance, TestContext.Current.CancellationToken);

        Assert.Equal("4.0.0", result.ProductVersion);
        foreach (var name in new[] { "provenance.json", "site-metadata.json" })
        {
            var content = File.ReadAllText(Path.Combine(generated, name));
            Assert.EndsWith(Environment.NewLine, content, StringComparison.Ordinal);
            Assert.False(content.EndsWith(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task PreservesCancellationFromVersionEvaluation()
    {
        using var directory = new TemporaryDirectory();
        var repositoryRoot = directory.Path;
        CreateProject(repositoryRoot, "src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj");
        var checkout = new CheckoutIdentity(repositoryRoot, Path.Combine(repositoryRoot, ".git"), CheckoutGitMarkerKind.Directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var writer = new DocumentationSiteMetadataWriter(new VersionProcessRunner(OutcomeKind.Cancelled));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteAsync(checkout,
            Path.Combine(repositoryRoot, "artifacts", "docs", "generated"),
            Path.Combine(repositoryRoot, "artifacts", "docs", "runs", "cancelled"),
            new GitProvenance(OutcomeKind.Success, new string('a', 40), "issue-97", true, null),
            cancellation.Token));
    }

    private static void CreateProject(string repositoryRoot, string relativePath)
    {
        var path = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<Project />");
    }

    private sealed class VersionProcessRunner(OutcomeKind outcome = OutcomeKind.Success) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessResult(outcome, outcome == OutcomeKind.Success ? 0 : null,
                outcome == OutcomeKind.Success ? ["4.0.0"] : [], [], 1, 0, TimeSpan.Zero,
                request.StandardOutputLogPath, request.StandardErrorLogPath, null));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "leancorpus-doc-metadata-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

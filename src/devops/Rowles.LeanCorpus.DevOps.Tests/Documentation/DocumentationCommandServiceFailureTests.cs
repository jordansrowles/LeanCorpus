using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Documentation;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Documentation;

public sealed class DocumentationCommandServiceFailureTests
{
    private const string SourceCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task PropagatesFailedDocsChildAndRetainsLinkedCaseEvidence()
    {
        using var checkout = TemporaryCheckout.Create();
        var paths = new ArtifactPaths(checkout.Root);
        var processRunner = new ProcessRunner();
        var repositoryRoot = FindRepositoryRoot();
        var configuration = new ConfigurationLoader(repositoryRoot).Load();
        var executor = new FailingTestExecutor(paths);
        var service = new DocumentationCommandService(
            paths,
            new ArtifactRunStore(paths, runCategory: "docs"),
            processRunner,
            new FixedProvenanceReader(),
            new DocumentationSiteMetadataWriter(processRunner),
            new TestRunPlanner(configuration, new AffectedSelectionService(processRunner)),
            executor,
            configuration);

        var result = await service.RunAsync(new DocumentationCommandRequest
        {
            Checkout = checkout.Identity,
            Action = "test",
            Framework = "net10.0",
            NoRestore = true,
            CommandLine = "managed docs test --framework net10.0 --no-restore"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.CommandFailure, result.CommandResult.Outcome);
        Assert.Equal(2, result.CommandResult.RawChildExitCode);
        Assert.Equal(RunState.Failed, result.Manifest.State);
        Assert.Contains(2, result.Manifest.ChildExitCodes);
        Assert.NotNull(executor.Request);
        Assert.Equal(result.Manifest.RunId, executor.Request.ParentRunId);
        Assert.True(executor.Request.RunnerOptions.ArtifactsEnabled);

        var childRunId = Guid.Parse(result.Manifest.Inputs["docsSiteTestRunId"]);
        Assert.Equal(executor.ChildRunId, childRunId);
        using var parentDocument = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(result.RunDirectory, "run.json"), TestContext.Current.CancellationToken));
        using var childDocument = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(paths.GetRunDirectory("test", childRunId), "run.json"), TestContext.Current.CancellationToken));
        using var childReport = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(paths.GetRunDirectory("test", childRunId), "report.json"), TestContext.Current.CancellationToken));

        Assert.Equal(1, parentDocument.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(childRunId.ToString("D"), parentDocument.RootElement.GetProperty("inputs").GetProperty("docsSiteTestRunId").GetString());
        Assert.Equal(1, childDocument.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(result.Manifest.RunId.ToString("D"), childDocument.RootElement.GetProperty("parentRunId").GetString());
        Assert.Equal("AssertionFailure", childReport.RootElement.GetProperty("attempts")[0].GetProperty("outcome").GetString());
        var failedCase = childReport.RootElement.GetProperty("attempts")[0].GetProperty("tests")[0];
        Assert.Equal("failed", failedCase.GetProperty("outcome").GetString());
        Assert.Contains("High Value Layouts Match Visual Baselines", failedCase.GetProperty("testName").GetString(), StringComparison.Ordinal);
        Assert.Contains("homepage-desktop", failedCase.GetProperty("errorMessage").GetString(), StringComparison.Ordinal);
        Assert.Contains("1.82%", failedCase.GetProperty("errorMessage").GetString(), StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
        => RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory).RepositoryRoot;

    private sealed class FixedProvenanceReader : IRepositoryProvenanceReader
    {
        public Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new GitProvenance(OutcomeKind.Success, SourceCommit, "issue-97", false, null));
    }

    private sealed class FailingTestExecutor(ArtifactPaths paths) : IManagedTestExecutor
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        public TestExecutorRequest? Request { get; private set; }

        public Guid ChildRunId { get; private set; }

        public async Task<TestExecutorResult> RunAsync(TestExecutorRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            var store = new ArtifactRunStore(paths, runCategory: "test");
            var started = await store.StartAsync("test",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["suite"] = "docs-site",
                    ["artifacts"] = "true"
                },
                parentRunId: request.ParentRunId,
                sourceCommit: SourceCommit,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            ChildRunId = started.RunId;
            var runDirectory = paths.GetRunDirectory("test", started.RunId);
            var report = new
            {
                schemaVersion = 1,
                runId = started.RunId,
                outcome = "CommandFailure",
                succeeded = false,
                attempts = new[]
                {
                    new
                    {
                        target = "docs-site/net10.0",
                        outcome = "AssertionFailure",
                        completed = true,
                        exitCode = 2,
                        tests = new[]
                        {
                            new
                            {
                                testName = "Docs Site: High Value Layouts Match Visual Baselines",
                                outcome = "failed",
                                errorMessage = "homepage-desktop visual difference 1.82% exceeds the 0.50% threshold."
                            }
                        }
                    }
                }
            };
            await File.WriteAllTextAsync(Path.Combine(runDirectory, "report.json"),
                JsonSerializer.Serialize(report, JsonOptions), cancellationToken).ConfigureAwait(false);
            var manifest = await store.FinishAsync(started, RunState.Failed, childExitCode: 2,
                artifacts: ["report.json"], failure: "One or more test targets failed.",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return new TestExecutorResult(
                new CommandResult(OutcomeKind.CommandFailure, "One or more test targets failed.", 2, runDirectory),
                runDirectory,
                null,
                manifest);
        }
    }

    private sealed class TemporaryCheckout : IDisposable
    {
        private TemporaryCheckout(string root) => Root = root;

        public string Root { get; }

        public CheckoutIdentity Identity => new(Root, Path.Combine(Root, ".git"), CheckoutGitMarkerKind.Directory);

        public static TemporaryCheckout Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "Issue 97 docs child " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            File.WriteAllText(Path.Combine(root, "Rowles.LeanCorpus.slnx"), "<Solution />\n");
            File.Copy(Path.Combine(FindRepositoryRoot(), "global.json"), Path.Combine(root, "global.json"));

            WriteVersionProject(root, "src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj", "4.0.0");
            WriteVersionProject(root, "src/devops/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj", "1.0.0");
            WriteDocumentationFixture(root);
            return new TemporaryCheckout(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }

        private static void WriteVersionProject(string root, string relativePath, string version)
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"<Project><PropertyGroup><Version>{version}</Version></PropertyGroup></Project>\n");
        }

        private static void WriteDocumentationFixture(string root)
        {
            var artifactRoot = Path.Combine(root, "artifacts", "docs");
            var contentRoot = Path.Combine(artifactRoot, "content");
            var sourceIndex = Path.Combine(root, "docs", "content", "index.md");
            Directory.CreateDirectory(Path.GetDirectoryName(sourceIndex)!);
            File.WriteAllText(sourceIndex, "# Source\n");

            var markdownPaths = new[]
            {
                "index.md",
                "articles/ADRs/index.md",
                "articles/ADRs/ADR040-bounded-field-length-bodies.md",
                "articles/ADRs/ADR041-physical-index-runtime-lifecycle.md",
                "articles/ADRs/Server/ADR-S005-aot-and-framework-compatibility-boundary.md",
                "changelog/index.md",
                "changelog/devops/unreleased.md",
                "benchmarks/overview.md",
                "benchmarks/qualification-audit.md"
            };
            var contributionUrl = $"https://github.com/jordansrowles/LeanCorpus/blob/{SourceCommit}/docs/content/index.md";
            foreach (var relativePath in markdownPaths)
            {
                var path = Path.Combine(contentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var body = relativePath == "changelog/index.md" ? "## LeanCorpus DevOps\n" : "# Test document\n";
                File.WriteAllText(path, $"---\ndocurl: '{contributionUrl}'\n---\n{body}");
            }

            WriteContentFile(contentRoot, "toc.yml", "items: []\n");
            WriteContentFile(contentRoot, "articles/ADRs/toc.yml", "items: []\n");
            WriteContentFile(contentRoot, "changelog/toc.yml", "name: 'LeanCorpus DevOps'\nitems: []\n");
            WriteContentFile(contentRoot, "benchmarks/toc.yml", "items: []\n");

            var apiRoot = Path.Combine(artifactRoot, "api");
            Directory.CreateDirectory(Path.Combine(apiRoot, "api"));
            File.WriteAllText(Path.Combine(apiRoot, "api", "index.html"), "<html><body>API</body></html>");
            var siteRoot = Path.Combine(artifactRoot, "site");
            Directory.CreateDirectory(Path.Combine(siteRoot, "api"));
            File.WriteAllText(Path.Combine(siteRoot, "index.html"), "<html><body>Home</body></html>");
            File.WriteAllText(Path.Combine(siteRoot, "api", "index.html"), "<html><body>API</body></html>");

            var generatedRoot = Path.Combine(artifactRoot, "generated");
            Directory.CreateDirectory(generatedRoot);
            File.WriteAllText(Path.Combine(generatedRoot, "provenance.json"), JsonSerializer.Serialize(new
            {
                commit = SourceCommit,
                devOpsVersion = "1.0.0"
            }));
            File.WriteAllText(Path.Combine(generatedRoot, "site-metadata.json"), JsonSerializer.Serialize(new
            {
                productVersion = "4.0.0",
                devOpsVersion = "1.0.0"
            }));
        }

        private static void WriteContentFile(string root, string relativePath, string content)
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }
}

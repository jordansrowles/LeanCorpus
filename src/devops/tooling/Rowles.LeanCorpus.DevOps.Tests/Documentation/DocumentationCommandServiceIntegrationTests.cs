using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Documentation;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.LeanCorpus.DevOps.Testing;
using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Tests.Documentation;

public sealed class DocumentationCommandServiceIntegrationTests
{
    [Fact(Explicit = true)]
    public async Task BuildsAndTestsCurrentRepositoryDocsThroughManagedServices()
    {
        var repositoryRoot = FindRepositoryRoot();
        var checkout = RepositoryPaths.Resolve(repositoryRoot, repositoryRoot, null);
        var paths = new ArtifactPaths(repositoryRoot);
        var processRunner = new ProcessRunner();
        var configuration = new ConfigurationLoader(repositoryRoot).Load();
        var service = new DocumentationCommandService(
            paths,
            new ArtifactRunStore(paths, runCategory: "docs"),
            processRunner,
            new GitRepositoryProvenanceReader(processRunner),
            new DocumentationSiteMetadataWriter(processRunner),
            new TestRunPlanner(configuration, new AffectedSelectionService(processRunner)),
            new TestExecutor(
                paths,
                new ArtifactRunStore(paths, runCategory: "test"),
                processRunner,
                new DotNetSdkResolver(processRunner),
                new GitRepositoryProvenanceReader(processRunner)),
            configuration,
            Console.WriteLine);

        var canonicalIndex = Path.Combine(repositoryRoot, "docs", "content", "index.md");
        var canonicalContentBefore = File.ReadAllText(canonicalIndex);
        var firstPrepare = await service.RunAsync(new DocumentationCommandRequest
        {
            Checkout = checkout,
            Action = "prepare",
            SkipBenchmarks = true,
            SkipCoverage = true,
            CommandLine = "managed docs prepare --skip-benchmarks --skip-coverage"
        }, TestContext.Current.CancellationToken);
        Assert.Equal(OutcomeKind.Success, firstPrepare.CommandResult.Outcome);
        File.WriteAllText(Path.Combine(paths.ArtifactRoot, "docs", "content", "stale-test-page.md"), "stale");
        var repeatedPrepare = await service.RunAsync(new DocumentationCommandRequest
        {
            Checkout = checkout,
            Action = "prepare",
            SkipBenchmarks = true,
            SkipCoverage = true,
            CommandLine = "managed docs prepare --skip-benchmarks --skip-coverage"
        }, TestContext.Current.CancellationToken);
        Assert.Equal(OutcomeKind.Success, repeatedPrepare.CommandResult.Outcome);
        Assert.Equal(ReadManifestFileHashes(firstPrepare.RunDirectory), ReadManifestFileHashes(repeatedPrepare.RunDirectory));
        Assert.False(File.Exists(Path.Combine(paths.ArtifactRoot, "docs", "content", "stale-test-page.md")));
        Assert.Equal(canonicalContentBefore, File.ReadAllText(canonicalIndex));

        var concurrentPrepares = await Task.WhenAll(
            service.RunAsync(new DocumentationCommandRequest
            {
                Checkout = checkout,
                Action = "prepare",
                SkipBenchmarks = true,
                SkipCoverage = true,
                CommandLine = "managed docs prepare --skip-benchmarks --skip-coverage"
            }, TestContext.Current.CancellationToken),
            service.RunAsync(new DocumentationCommandRequest
            {
                Checkout = checkout,
                Action = "prepare",
                SkipBenchmarks = true,
                SkipCoverage = true,
                CommandLine = "managed docs prepare --skip-benchmarks --skip-coverage"
            }, TestContext.Current.CancellationToken));
        Assert.All(concurrentPrepares, result => Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome));
        Assert.Equal(ReadManifestFileHashes(concurrentPrepares[0].RunDirectory), ReadManifestFileHashes(concurrentPrepares[1].RunDirectory));

        var build = await service.RunAsync(new DocumentationCommandRequest
        {
            Checkout = checkout,
            Action = "build",
            SkipBenchmarks = true,
            SkipCoverage = true,
            CommandLine = "managed docs build --skip-benchmarks --skip-coverage"
        }, TestContext.Current.CancellationToken);

        Assert.True(build.CommandResult.Outcome == OutcomeKind.Success,
            $"Managed docs build returned {build.CommandResult.Outcome}: {build.CommandResult.Message} {build.CommandResult.Details}");
        Assert.True(File.Exists(Path.Combine(paths.ArtifactRoot, "docs", "site", "index.html")));
        Assert.True(File.Exists(Path.Combine(paths.ArtifactRoot, "docs", "content", "benchmarks", "qualification-audit.md")));
        Assert.True(File.Exists(Path.Combine(build.RunDirectory, "content-manifest.json")));
        Assert.Equal(RunState.Succeeded, build.Manifest.State);

        var test = await service.RunAsync(new DocumentationCommandRequest
        {
            Checkout = checkout,
            Action = "test",
            Framework = "net10.0",
            NoRestore = true,
            CommandLine = "managed docs test --framework net10.0 --no-restore"
        }, TestContext.Current.CancellationToken);

        Assert.True(test.CommandResult.Outcome == OutcomeKind.Success,
            $"Managed docs test returned {test.CommandResult.Outcome}: {test.CommandResult.Message} {test.CommandResult.Details}");
        Assert.Equal(RunState.Succeeded, test.Manifest.State);
        Assert.NotNull(test.StaticValidation);
        Assert.Equal(218, test.StaticValidation.MarkdownPages);
        Assert.Equal(1746, test.StaticValidation.HtmlPages);
        var childRunId = Guid.Parse(test.Manifest.Inputs["docsSiteTestRunId"]);
        using var childManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(paths.ArtifactRoot, "test", "runs", childRunId.ToString("D"), "run.json")));
        Assert.Equal(1, test.Manifest.SchemaVersion);
        Assert.Equal(test.Manifest.RunId, Guid.Parse(childManifest.RootElement.GetProperty("parentRunId").GetString()!));
        Assert.Equal(1, childManifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("true", childManifest.RootElement.GetProperty("inputs").GetProperty("noRestore").GetString());
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(Environment.CurrentDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "docs", "site-config", "imports.json"))) return current.FullName;
        }
        throw new DirectoryNotFoundException("Could not find the LeanCorpus repository from the test working directory.");
    }

    private static string[] ReadManifestFileHashes(string runDirectory)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "content-manifest.json")));
        return document.RootElement.GetProperty("files").EnumerateArray()
            .Select(static item => $"{item.GetProperty("path").GetString()}:{item.GetProperty("length").GetInt64()}:{item.GetProperty("sha256").GetString()}")
            .ToArray();
    }

}

using System.Text.Json;
using System.Text.RegularExpressions;
using Rowles.LeanCorpus.DevOps.Documentation;

namespace Rowles.LeanCorpus.DevOps.Tests.Documentation;

public sealed class RepositoryDocumentationImporterTests
{
    [Fact]
    public void ImportsCanonicalDocumentsAndRewritesLinksAgainstTheStagedTree()
    {
        using var directory = new TemporaryDirectory();
        var repositoryRoot = directory.Path;
        var docsRoot = Path.Combine(repositoryRoot, "docs", "content");
        var stagedContent = Path.Combine(repositoryRoot, "artifacts", "docs", "content");
        Directory.CreateDirectory(Path.Combine(docsRoot, "guides"));
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "devops"));
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "changelog"));
        Directory.CreateDirectory(Path.Combine(stagedContent, "guides"));

        File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), """
            # Repository

            [Guide](docs/content/guides/start.md)
            [Guide with query](docs/content/guides/quick%20start.md?view=full#heading)
            [DevOps](src/devops/README.md)
            [Contributing](CONTRIBUTING.md)
            [History](changelog/unreleased.md)
            """ + Environment.NewLine);
        File.WriteAllText(Path.Combine(repositoryRoot, "CONTRIBUTING.md"), "# Contributing");
        File.WriteAllText(Path.Combine(repositoryRoot, "src", "devops", "README.md"), "# DevOps");
        File.WriteAllText(Path.Combine(repositoryRoot, "changelog", "unreleased.md"), "# Unreleased");
        File.WriteAllText(Path.Combine(docsRoot, "guides", "start.md"), """
            # Start

            [Repository](../../../README.md)
            """ + Environment.NewLine);
        File.WriteAllText(Path.Combine(docsRoot, "guides", "quick start.md"), "# Quick start");
        File.Copy(Path.Combine(docsRoot, "guides", "start.md"), Path.Combine(stagedContent, "guides", "start.md"));
        File.Copy(Path.Combine(docsRoot, "guides", "quick start.md"), Path.Combine(stagedContent, "guides", "quick start.md"));

        var manifestPath = Path.Combine(repositoryRoot, "docs", "site-config", "imports.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            imports = new[]
            {
                new { source = "README.md", destination = "generated/repository-overview.md" },
                new { source = "CONTRIBUTING.md", destination = "generated/contributors/contributing.md" },
                new { source = "src/devops/README.md", destination = "generated/contributors/devops-projects.md" }
            }
        }));

        const string commit = "0123456789abcdef0123456789abcdef01234567";
        var importer = new RepositoryDocumentationImporter();

        var importedCount = importer.Import(repositoryRoot, stagedContent, docsRoot, manifestPath, commit);

        Assert.Equal(3, importedCount);
        var importedOverview = File.ReadAllText(Path.Combine(stagedContent, "generated", "repository-overview.md"));
        Assert.Contains("generated from [README.md](https://github.com/jordansrowles/LeanCorpus/blob/" + commit + "/README.md)", importedOverview, StringComparison.Ordinal);
        Assert.Contains("[Guide](../guides/start.md)", importedOverview, StringComparison.Ordinal);
        Assert.Contains("[Guide with query](../guides/quick%20start.md?view=full#heading)", importedOverview, StringComparison.Ordinal);
        Assert.Contains("[DevOps](contributors/devops-projects.md)", importedOverview, StringComparison.Ordinal);
        Assert.Contains("[Contributing](contributors/contributing.md)", importedOverview, StringComparison.Ordinal);
        Assert.Contains("[History](https://github.com/jordansrowles/LeanCorpus/blob/" + commit + "/changelog/unreleased.md)", importedOverview, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine + Environment.NewLine, importedOverview, StringComparison.Ordinal);

        var stagedGuide = File.ReadAllText(Path.Combine(stagedContent, "guides", "start.md"));
        Assert.Contains("[Repository](../generated/repository-overview.md)", stagedGuide, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine + Environment.NewLine, stagedGuide, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAuthoredDestinationCollisionBeforeWritingAnyImports()
    {
        using var directory = new TemporaryDirectory();
        var repositoryRoot = directory.Path;
        var docsRoot = Path.Combine(repositoryRoot, "docs", "content");
        var stagedContent = Path.Combine(repositoryRoot, "artifacts", "docs", "content");
        Directory.CreateDirectory(docsRoot);
        Directory.CreateDirectory(stagedContent);
        File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "# Repository");
        File.WriteAllText(Path.Combine(repositoryRoot, "CONTRIBUTING.md"), "# Contributing");
        var collisionPath = Path.Combine(stagedContent, "generated", "authored.md");
        Directory.CreateDirectory(Path.GetDirectoryName(collisionPath)!);
        File.WriteAllText(collisionPath, "authored content");

        var manifestPath = Path.Combine(repositoryRoot, "imports.json");
        File.WriteAllText(manifestPath, """
            {
              "schemaVersion": 1,
              "imports": [
                { "source": "README.md", "destination": "generated/first.md" },
                { "source": "CONTRIBUTING.md", "destination": "generated/authored.md" }
              ]
            }
            """);

        var importer = new RepositoryDocumentationImporter();

        Assert.Throws<InvalidDataException>(() => importer.Import(repositoryRoot, stagedContent, docsRoot, manifestPath,
            "0123456789abcdef0123456789abcdef01234567"));

        Assert.False(File.Exists(Path.Combine(stagedContent, "generated", "first.md")));
        Assert.Equal("authored content", File.ReadAllText(collisionPath));
    }

    [Fact]
    public void RejectsDestinationTraversalBeforeWritingAnyImports()
    {
        using var directory = new TemporaryDirectory();
        var repositoryRoot = directory.Path;
        var docsRoot = Path.Combine(repositoryRoot, "docs", "content");
        var stagedContent = Path.Combine(repositoryRoot, "artifacts", "docs", "content");
        Directory.CreateDirectory(docsRoot);
        Directory.CreateDirectory(stagedContent);
        File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "# Repository");
        var outsidePath = Path.Combine(repositoryRoot, "artifacts", "outside.md");
        var manifestPath = Path.Combine(repositoryRoot, "imports.json");
        File.WriteAllText(manifestPath, """
            {
              "schemaVersion": 1,
              "imports": [
                { "source": "README.md", "destination": "../outside.md" }
              ]
            }
            """);

        var importer = new RepositoryDocumentationImporter();

        Assert.Throws<InvalidDataException>(() => importer.Import(repositoryRoot, stagedContent, docsRoot, manifestPath,
            "0123456789abcdef0123456789abcdef01234567"));

        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public void RejectsDuplicateManifestPropertiesBeforeWritingImports()
    {
        using var directory = new TemporaryDirectory();
        var repositoryRoot = directory.Path;
        var docsRoot = Path.Combine(repositoryRoot, "docs", "content");
        var stagedContent = Path.Combine(repositoryRoot, "artifacts", "docs", "content");
        Directory.CreateDirectory(docsRoot);
        Directory.CreateDirectory(stagedContent);
        File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "# Repository");
        var manifestPath = Path.Combine(repositoryRoot, "imports.json");
        File.WriteAllText(manifestPath, """
            {
              "schemaVersion": 1,
              "schemaVersion": 1,
              "imports": [
                { "source": "README.md", "destination": "generated/repository.md" }
              ]
            }
            """);

        var importer = new RepositoryDocumentationImporter();

        Assert.Throws<InvalidDataException>(() => importer.Import(repositoryRoot, stagedContent, docsRoot, manifestPath,
            "0123456789abcdef0123456789abcdef01234567"));

        Assert.False(File.Exists(Path.Combine(stagedContent, "generated", "repository.md")));
    }

    [Fact]
    public void GeneratesIndependentChangelogNavigationWithUnreleasedFirst()
    {
        using var directory = new TemporaryDirectory();
        var root = Path.Combine(directory.Path, "changelog");
        Directory.CreateDirectory(Path.Combine(root, "server"));
        Directory.CreateDirectory(Path.Combine(root, "devops"));
        File.WriteAllText(Path.Combine(root, "1.9.0.md"), "# Core 1.9.0");
        File.WriteAllText(Path.Combine(root, "2.0.0.md"), "# Core 2.0.0");
        File.WriteAllText(Path.Combine(root, "unreleased.md"), "# Next release");
        File.WriteAllText(Path.Combine(root, "server", "unreleased.md"), "# Server next");
        File.WriteAllText(Path.Combine(root, "devops", "unreleased.md"), "# DevOps next");

        new DocumentationSectionCatalogWriter().Generate(root, "Changelog", "Versioned project release notes.", separateServerChangelog: true);

        var index = File.ReadAllText(Path.Combine(root, "index.md"));
        Assert.True(index.IndexOf("## LeanCorpus and Rowles.Text", StringComparison.Ordinal) < index.IndexOf("## Community Server", StringComparison.Ordinal));
        Assert.True(index.IndexOf("## Community Server", StringComparison.Ordinal) < index.IndexOf("## LeanCorpus DevOps", StringComparison.Ordinal));
        Assert.True(index.IndexOf("[Next release](unreleased.md)", StringComparison.Ordinal) < index.IndexOf("[Core 2.0.0](2.0.0.md)", StringComparison.Ordinal));

        var toc = File.ReadAllText(Path.Combine(root, "toc.yml"));
        Assert.Contains("name: 'LeanCorpus DevOps'", toc, StringComparison.Ordinal);
        Assert.Contains("href: 'devops/unreleased.md'", toc, StringComparison.Ordinal);
    }

    [Fact]
    public void StagesCanonicalContentAndAddsRevisionPinnedContributionLinks()
    {
        using var directory = new TemporaryDirectory();
        var repositoryRoot = directory.Path;
        var docsRoot = Path.Combine(repositoryRoot, "docs", "content");
        var stagedContent = Path.Combine(repositoryRoot, "artifacts", "docs", "content");
        Directory.CreateDirectory(docsRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "# Repository");
        var authoredPath = Path.Combine(docsRoot, "start.md");
        const string authoredContent = "# Start\n";
        File.WriteAllText(authoredPath, authoredContent);
        var manifestPath = Path.Combine(repositoryRoot, "docs", "site-config", "imports.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, """
            {
              "schemaVersion": 1,
              "imports": [
                { "source": "README.md", "destination": "generated/repository.md" }
              ]
            }
            """);

        var stager = new DocumentationContentStager();
        stager.CopyCanonicalContent(docsRoot, stagedContent);
        new RepositoryDocumentationImporter().Import(repositoryRoot, stagedContent, docsRoot, manifestPath,
            "0123456789abcdef0123456789abcdef01234567");
        stager.SetContributionMetadata(repositoryRoot, stagedContent, manifestPath,
            "0123456789abcdef0123456789abcdef01234567");

        var stagedAuthored = File.ReadAllText(Path.Combine(stagedContent, "start.md"));
        Assert.Contains("docurl: 'https://github.com/jordansrowles/LeanCorpus/blob/0123456789abcdef0123456789abcdef01234567/docs/content/start.md'", stagedAuthored, StringComparison.Ordinal);
        Assert.Equal(authoredContent, File.ReadAllText(authoredPath));
        Assert.Contains("docurl: 'https://github.com/jordansrowles/LeanCorpus/blob/0123456789abcdef0123456789abcdef01234567/README.md'",
            File.ReadAllText(Path.Combine(stagedContent, "generated", "repository.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void CopiesOnlyPublishedChangelogMarkdownAndKeepsOtherStagedFiles()
    {
        using var directory = new TemporaryDirectory();
        var repositoryRoot = directory.Path;
        var source = Path.Combine(repositoryRoot, "changelog");
        var content = Path.Combine(repositoryRoot, "artifacts", "docs", "content");
        var staged = Path.Combine(content, "changelog");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(staged);
        File.WriteAllText(Path.Combine(source, "unreleased.md"), "# Unreleased");
        File.WriteAllText(Path.Combine(source, "_template.md"), "# Template");
        File.WriteAllText(Path.Combine(source, "_vnext.md"), "# Next");
        File.WriteAllText(Path.Combine(staged, "index.md"), "# Authored collision");
        File.WriteAllText(Path.Combine(staged, "stale.md"), "# Stale");

        new DocumentationContentStager().CopyChangelog(repositoryRoot, content);

        Assert.True(File.Exists(Path.Combine(staged, "index.md")));
        Assert.True(File.Exists(Path.Combine(staged, "unreleased.md")));
        Assert.False(File.Exists(Path.Combine(staged, "_template.md")));
        Assert.False(File.Exists(Path.Combine(staged, "_vnext.md")));
        Assert.False(File.Exists(Path.Combine(staged, "stale.md")));
    }

    [Fact]
    public void GeneratesAdrGraphIndexAndStagedRelationshipLinksWithoutEditingSources()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "source");
        var staged = Path.Combine(directory.Path, "staged");
        Directory.CreateDirectory(Path.Combine(source, "Server"));
        Directory.CreateDirectory(Path.Combine(staged, "Server"));
        var priorSource = Path.Combine(source, "ADR001-prior.md");
        var currentSource = Path.Combine(source, "ADR002-current.md");
        var serverSource = Path.Combine(source, "Server", "ADR-S001-server.md");
        var priorContent = Adr("ADR001", "Prior decision", "2020-01-01", "Superseded", "1.0.0",
            "superseded-by: [ADR002]", "last-updated: 2020-02-01",
            "| 2020-02-01 | Superseded | Replaced by the current decision | [PR 2](https://github.com/example/2) |");
        var currentContent = Adr("ADR002", "Current decision", "2021-01-01", "Accepted", "2.0.0",
            "supersedes: [ADR001]", null, null);
        var serverContent = Adr("ADR-S001", "Server decision", "2022-01-01", "Accepted", "2.1.0",
            null, null, null);
        File.WriteAllText(priorSource, priorContent);
        File.WriteAllText(currentSource, currentContent);
        File.WriteAllText(serverSource, serverContent);
        File.Copy(priorSource, Path.Combine(staged, Path.GetFileName(priorSource)));
        File.Copy(currentSource, Path.Combine(staged, Path.GetFileName(currentSource)));
        File.Copy(serverSource, Path.Combine(staged, "Server", Path.GetFileName(serverSource)));
        var outputPath = Path.Combine(staged, "index.md");

        var count = new AdrIndexGenerator().Generate(source, outputPath, staged);

        Assert.Equal(3, count);
        var index = File.ReadAllText(outputPath);
        Assert.Contains("<a href=\"ADR002-current.md\">ADR002</a>", index, StringComparison.Ordinal);
        Assert.Contains("Community Server decisions", index, StringComparison.Ordinal);
        Assert.Contains("href: Server/ADR-S001-server.md", File.ReadAllText(Path.Combine(staged, "toc.yml")), StringComparison.Ordinal);
        var stagedPrior = File.ReadAllText(Path.Combine(staged, "ADR001-prior.md"));
        Assert.Contains("## Relationships", stagedPrior, StringComparison.Ordinal);
        Assert.Contains("- **Superseded by:** <a href=\"ADR002-current.md\">ADR002</a>", stagedPrior, StringComparison.Ordinal);
        Assert.DoesNotContain("## Relationships", priorContent, StringComparison.Ordinal);
        Assert.Equal(priorContent, File.ReadAllText(priorSource));

        var repeatedCount = new AdrIndexGenerator().Generate(source, outputPath, staged);
        Assert.Equal(3, repeatedCount);
        Assert.Single(Regex.Matches(File.ReadAllText(Path.Combine(staged, "ADR001-prior.md")), "(?m)^## Relationships$").Cast<Match>());
    }

    [Fact]
    public void GeneratesEmptyBenchmarkOverviewAndKeepsPinnedRuntimeAssets()
    {
        using var directory = new TemporaryDirectory();
        var runs = Path.Combine(directory.Path, "runs");
        var output = Path.Combine(directory.Path, "content", "benchmarks");
        var chartDirectory = Path.Combine(directory.Path, "diagrams", "node_modules", "chart.js", "dist");
        var chartScript = Path.Combine(directory.Path, "benchmark-charts.js");
        Directory.CreateDirectory(runs);
        Directory.CreateDirectory(chartDirectory);
        File.WriteAllText(Path.Combine(chartDirectory, "chart.umd.js"), "window.Chart = {}; ");
        File.WriteAllText(chartScript, "window.benchmarkCharts = true;" + Environment.NewLine);

        var result = new BenchmarkDocumentationGenerator().Generate(runs, output,
            Path.Combine(chartDirectory, "chart.umd.js"), chartScript, skipRunScan: true);

        Assert.Equal(0, result.PageCount);
        Assert.Contains("No benchmark reports are available", File.ReadAllText(Path.Combine(output, "overview.md")), StringComparison.Ordinal);
        Assert.DoesNotContain("items:", File.ReadAllText(Path.Combine(output, "toc.yml")), StringComparison.Ordinal);
        Assert.Equal("window.Chart = {}; ", File.ReadAllText(Path.Combine(output, "chart.umd.js")));
        Assert.Equal("window.benchmarkCharts = true;" + Environment.NewLine + Environment.NewLine,
            File.ReadAllText(Path.Combine(output, "benchmark-charts.js")));
    }

    [Fact]
    public void GeneratesCoreBenchmarkPageFromTheNewestSuccessfulRetainedRun()
    {
        using var directory = new TemporaryDirectory();
        var runs = Path.Combine(directory.Path, "runs");
        var run = Path.Combine(runs, "run-1");
        var suiteResults = Path.Combine(run, "core", "query", "results");
        var output = Path.Combine(directory.Path, "content", "benchmarks");
        var chartDirectory = Path.Combine(directory.Path, "diagrams", "node_modules", "chart.js", "dist");
        var chartScript = Path.Combine(directory.Path, "benchmark-charts.js");
        Directory.CreateDirectory(suiteResults);
        Directory.CreateDirectory(chartDirectory);
        File.WriteAllText(Path.Combine(chartDirectory, "chart.umd.js"), "chart-bundle");
        File.WriteAllText(chartScript, "chart-runtime");
        File.WriteAllText(Path.Combine(run, "run.json"), """
            { "status": "Passed", "completedAtUtc": "2026-01-02T03:04:05Z", "machine": "test-host" }
            """);
        File.WriteAllText(Path.Combine(run, "run-report.json"), """
            { "projects": [ { "project": "core", "status": "Passed", "path": "core" } ] }
            """);
        File.WriteAllText(Path.Combine(run, "core", "report.json"), """
            {
              "totalBenchmarkCount": 1,
              "generatedAtUtc": "2026-01-02T03:00:00Z",
              "commitHash": "0123456789abcdef",
              "dotnetVersion": "10.0.0",
              "provenance": { "machineName": "test-host", "effectiveDocCount": 42 },
              "suites": [ { "suiteName": "query", "failedBenchmarkCount": 0, "missingBenchmarkCount": 0 } ]
            }
            """);
        File.WriteAllText(Path.Combine(suiteResults, "sample-report-github.md"), "Environment details\n\n| Method | Mean |\n| --- | --- |\n| Search | 1 ms |\n");
        File.WriteAllText(Path.Combine(suiteResults, "sample-report-full.json"), "{\"Benchmarks\":[]}");

        var result = new BenchmarkDocumentationGenerator().Generate(runs, output,
            Path.Combine(chartDirectory, "chart.umd.js"), chartScript);

        Assert.Equal(1, result.PageCount);
        var page = File.ReadAllText(Path.Combine(output, "query.md"));
        Assert.Contains("# Term queries", page, StringComparison.Ordinal);
        Assert.Contains("Commit** ``0123456``", page, StringComparison.Ordinal);
        Assert.Contains("42 docs", page, StringComparison.Ordinal);
        Assert.Contains("chart-scale-query", page, StringComparison.Ordinal);
        Assert.Equal("{\"Benchmarks\":[]}", File.ReadAllText(Path.Combine(output, "query.json")));
        Assert.Contains("[Term queries](query.md)", File.ReadAllText(Path.Combine(output, "overview.md")), StringComparison.Ordinal);
    }

    private static string Adr(string id, string title, string date, string status, string version,
        string? relationship, string? lastUpdated, string? extraChange)
    {
        var lines = new List<string>
        {
            "---",
            $"adr: {id}",
            $"title: '{title}'",
            $"date: {date}"
        };
        if (lastUpdated is not null) lines.Add(lastUpdated);
        lines.Add($"status: {status}");
        lines.Add($"version-added: {version}");
        lines.Add("summary: A fixture decision.");
        lines.Add("areas: [Architecture]");
        if (relationship is not null) lines.Add(relationship);
        lines.Add("---");
        lines.Add("## Context");
        lines.Add("Fixture context.");
        lines.Add("## Changes");
        lines.Add(string.Empty);
        lines.Add("| Date | Change | Description | Reference |");
        lines.Add("| --- | --- | --- | --- |");
        lines.Add($"| {date} | Accepted | Initial decision | [PR 1](https://github.com/example/1) |");
        if (extraChange is not null) lines.Add(extraChange);
        lines.Add(string.Empty);
        lines.Add("## Decision");
        lines.Add("Fixture decision.");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "leancorpus-docs-import-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

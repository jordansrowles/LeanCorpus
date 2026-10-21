using System.Collections.Immutable;
using System.Diagnostics;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class AffectedSelectionServiceTests
{
    [Fact]
    public void MapsAndDeduplicatesUnicodePathsAcrossMultipleSuites()
    {
        var suites = new Dictionary<string, TestSuiteConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["core"] = Suite(),
            ["text"] = Suite()
        };
        var areas = new Dictionary<string, CodeAreaConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["cross-library"] = new(["src/core/**/*.cs"], ["core:Search", "text:Analysers"])
        };

        var selection = AffectedSelectionService.MapChangedPaths(
            suites,
            areas,
            ["src/core/Search/Ångström parser.cs", "src/core/Search/Ångström parser.cs"]);

        Assert.Equal(["src/core/Search/Ångström parser.cs"], selection.Paths);
        Assert.Equal(["core:Search", "text:Analysers"], selection.Targets);
        Assert.Equal(["Search"], selection.AreasBySuite["core"]);
        Assert.Equal(["Analysers"], selection.AreasBySuite["text"]);
    }

    [Fact]
    public void RefusesUnmappedPathsAndCodeAreasThatProduceNoTargets()
    {
        var suites = new Dictionary<string, TestSuiteConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["core"] = Suite()
        };
        var areas = new Dictionary<string, CodeAreaConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["docs-only"] = new(["docs/**/*.md"], [])
        };

        var unmapped = Assert.Throws<InvalidOperationException>(() => AffectedSelectionService.MapChangedPaths(
            suites, areas, ["unknown/changed.cs"]));
        var noTargets = Assert.Throws<InvalidOperationException>(() => AffectedSelectionService.MapChangedPaths(
            suites, areas, ["docs/content/readme.md"]));

        Assert.Contains("no code-area mapping", unmapped.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no executable test areas", noTargets.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsMappingsToUnknownSuites()
    {
        var suites = new Dictionary<string, TestSuiteConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["core"] = Suite()
        };
        var areas = new Dictionary<string, CodeAreaConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["broken"] = new(["src/core/**/*.cs"], ["missing:Index"])
        };

        var exception = Assert.Throws<InvalidOperationException>(() => AffectedSelectionService.MapChangedPaths(
            suites, areas, ["src/core/file.cs"]));

        Assert.Contains("references unknown test suite 'missing'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchesPathsUsingTheHostFilesystemCaseRules()
    {
        var suites = new Dictionary<string, TestSuiteConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["core"] = Suite()
        };
        var areas = new Dictionary<string, CodeAreaConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["case"] = new(["src/Core/*.cs"], ["core:Index"])
        };

        if (OperatingSystem.IsWindows())
        {
            var selection = AffectedSelectionService.MapChangedPaths(suites, areas, ["src/core/index.cs"]);
            Assert.Equal(["core:Index"], selection.Targets);
        }
        else
        {
            var exception = Assert.Throws<InvalidOperationException>(() => AffectedSelectionService.MapChangedPaths(
                suites, areas, ["src/core/index.cs"]));
            Assert.Contains("no code-area mapping", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ReadsStagedUnstagedDeletedAndUntrackedPathsAsNulDelimitedUtf8()
    {
        using var repository = await TemporaryGitRepository.CreateAsync();
        await repository.WriteAsync("src/core/Index/staged.cs", "class Staged { }\n");
        await repository.WriteAsync("src/core/Search/changed.cs", "class Changed { }\n");
        await repository.WriteAsync("src/core/Store/removed.cs", "class Removed { }\n");
        await repository.CommitAsync("add files");

        await repository.WriteAsync("src/core/Index/staged.cs", "class Staged { int Value => 1; }\n");
        await repository.RunGitAsync("add", "--", "src/core/Index/staged.cs");
        await repository.WriteAsync("src/core/Search/changed.cs", "class Changed { int Value => 2; }\n");
        File.Delete(repository.FullPath("src/core/Store/removed.cs"));
        var unusualPath = OperatingSystem.IsWindows()
            ? "src/core/Store/Ångström parser.cs"
            : "src/core/Store/line\nbreak\tΩ.cs";
        await repository.WriteAsync(unusualPath, "class Untracked { }\n");

        var selection = await new AffectedSelectionService(new ProcessRunner()).ResolveAsync(
            repository.Path,
            Suites(("core", Suite())),
            new Dictionary<string, CodeAreaConfiguration>(StringComparer.OrdinalIgnoreCase)
            {
                ["core-files"] = new(["src/core/**/*.cs"], ["core:Index"])
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("dirty", selection.Mode);
        Assert.Equal(repository.Head, selection.Head);
        var expectedPaths = new[]
        {
            "src/core/Index/staged.cs",
            "src/core/Search/changed.cs",
            "src/core/Store/removed.cs",
            unusualPath
        }.Order(StringComparer.Ordinal);
        Assert.Equal(expectedPaths, selection.Paths);
        Assert.Equal(["core:Index"], selection.Targets);
    }

    [Fact]
    public async Task CommitSelectionUsesTheFirstParentAndExcludesDirtyFiles()
    {
        using var repository = await TemporaryGitRepository.CreateAsync();
        await repository.WriteAsync("src/core/Search/base.cs", "class Base { }\n");
        await repository.CommitAsync("base");
        var firstParent = repository.Head;
        await repository.WriteAsync("src/core/Search/commit.cs", "class Committed { }\n");
        await repository.CommitAsync("commit selection");
        var commit = repository.Head;
        await repository.WriteAsync("src/core/Search/dirty.cs", "class Dirty { }\n");

        var selection = await new AffectedSelectionService(new ProcessRunner()).ResolveAsync(
            repository.Path,
            Suites(("core", Suite())),
            CoreSearchMapping(),
            commit: "HEAD",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("commit", selection.Mode);
        Assert.Equal(commit, selection.ResolvedCommit);
        Assert.Equal(firstParent, selection.FirstParent);
        Assert.Equal(["src/core/Search/commit.cs"], selection.Paths);
    }

    [Fact]
    public async Task RangeSelectionExcludesDirtyFilesAndRejectsMalformedRanges()
    {
        using var repository = await TemporaryGitRepository.CreateAsync();
        await repository.WriteAsync("src/core/Search/base.cs", "class Base { }\n");
        await repository.CommitAsync("base");
        var baseCommit = repository.Head;
        await repository.WriteAsync("src/core/Search/one.cs", "class One { }\n");
        await repository.CommitAsync("one");
        await repository.WriteAsync("src/core/Search/two.cs", "class Two { }\n");
        await repository.CommitAsync("two");
        var endCommit = repository.Head;
        await repository.WriteAsync("src/core/Search/dirty.cs", "class Dirty { }\n");

        var service = new AffectedSelectionService(new ProcessRunner());
        var selection = await service.ResolveAsync(
            repository.Path,
            Suites(("core", Suite())),
            CoreSearchMapping(),
            range: $"{baseCommit}..{endCommit}",
            cancellationToken: TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAsync(
            repository.Path,
            Suites(("core", Suite())),
            CoreSearchMapping(),
            range: "HEAD",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("range", selection.Mode);
        Assert.Equal($"{baseCommit}..{endCommit}", selection.ResolvedCommit);
        Assert.Equal(["src/core/Search/one.cs", "src/core/Search/two.cs"], selection.Paths);
        Assert.Contains("Invalid range", exception.Message, StringComparison.Ordinal);
    }

    private static TestSuiteConfiguration Suite()
        => new(
            "Fixture suite",
            "src/devops/Fixture.Tests.csproj",
            "Mtp",
            ImmutableArray.Create("net10.0"),
            null,
            false,
            ImmutableArray.Create("Trx"),
            null,
            null,
            null);

    private static IReadOnlyDictionary<string, TestSuiteConfiguration> Suites(
        params (string Key, TestSuiteConfiguration Configuration)[] suites)
        => suites.ToDictionary(static item => item.Key, static item => item.Configuration, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, CodeAreaConfiguration> CoreSearchMapping()
        => new Dictionary<string, CodeAreaConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["core-search"] = new(["src/core/Search/**/*.cs"], ["core:Search"])
        };

    private sealed class TemporaryGitRepository : IDisposable
    {
        private readonly string _path;

        private TemporaryGitRepository(string path) => _path = Path = path;

        public string Path { get; }
        public string Head => File.ReadAllText(System.IO.Path.Combine(Path, ".git", "refs", "heads", "main")).Trim();

        public static async Task<TemporaryGitRepository> CreateAsync()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LeanCorpus affected git " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var repository = new TemporaryGitRepository(path);
            await repository.RunGitAsync("init", "--initial-branch=main");
            await repository.RunGitAsync("config", "user.name", "Affected Test");
            await repository.RunGitAsync("config", "user.email", "affected-test@example.invalid");
            return repository;
        }

        public string FullPath(string relativePath) => System.IO.Path.Combine(_path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

        public async Task WriteAsync(string relativePath, string content)
        {
            var path = FullPath(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
        }

        public async Task CommitAsync(string message)
        {
            await RunGitAsync("add", "--all");
            await RunGitAsync("-c", "user.name=Affected Test", "-c", "user.email=affected-test@example.invalid", "commit", "-m", message);
        }

        public async Task RunGitAsync(params string[] arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git")
                {
                    WorkingDirectory = _path,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var error = await stderr;
            _ = await stdout;
            Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
        }

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}

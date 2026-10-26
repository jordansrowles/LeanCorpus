using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Commands;

public sealed class BrowserTestWorkflowContractTests
{
    private const string LinuxQualificationCondition = "github.event_name == 'workflow_dispatch' || github.event_name == 'pull_request' || github.ref == 'refs/heads/main' || startsWith(github.ref, 'refs/heads/stabilise/') || startsWith(github.event.head_commit.message, '[deploy]')";

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net11.0")]
    public void GenericAllSelectionExcludesBrowserSuitesAndRetainsEveryOtherSuite(string framework)
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var resolver = new TestTargetResolver(configuration.TestSuites);

        var targets = resolver.Resolve(new TestTargetRequest
        {
            Suites = ["all"],
            Framework = framework,
            FrameworkExplicit = true,
            ContinuousIntegration = true
        });

        string[] expected = configuration.TestSuites.Keys
            .Except(["docs-site", "server-orchard-functional"], StringComparer.OrdinalIgnoreCase)
            .Where(suite => configuration.TestSuites[suite].Frameworks.Contains(framework, StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(expected, targets.Select(static target => target.Suite).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase));
        Assert.Contains("server-orchard", targets.Select(static target => target.Suite), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("server-orchard-functional", targets.Select(static target => target.Suite), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("docs-site", targets.Select(static target => target.Suite), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EveryPlaywrightProjectIsClassifiedAndExplicitBrowserTargetsRetainTheirRequirements()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        Assert.NotNull(typeof(TestSuiteConfiguration).GetProperty("RequiresBrowser"));
        Assert.NotNull(typeof(TestTarget).GetProperty("RequiresBrowser"));

        var browserProjects = FindPlaywrightProjects(checkout.RepositoryRoot);
        var registeredBrowserProjects = configuration.TestSuites
            .Where(static pair => pair.Value.RequiresBrowser)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value.Project, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(browserProjects.OrderBy(static path => path, StringComparer.Ordinal),
            registeredBrowserProjects.Values.OrderBy(static path => path, StringComparer.Ordinal));

        var resolver = new TestTargetResolver(configuration.TestSuites);
        foreach (var (suiteKey, suite) in configuration.TestSuites.Where(static pair => pair.Value.RequiresBrowser))
        {
            Assert.False(suite.IncludeInAll);
            foreach (string framework in suite.Frameworks)
            {
                var target = Assert.Single(resolver.Resolve(new TestTargetRequest
                {
                    Suites = [suiteKey],
                    Framework = framework,
                    FrameworkExplicit = true,
                    ContinuousIntegration = true
                }));

                Assert.Equal($"{suiteKey}/{framework}", target.Key);
                Assert.True(target.RequiresBrowser);
                if (suiteKey == "server-orchard-functional")
                {
                    Assert.Contains("FullyQualifiedName!~Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests.ProviderPerformanceTests.", target.Filter, StringComparison.Ordinal);
                }

                var temporaryDirectory = Path.Combine(Path.GetTempPath(), "LeanCorpus browser selection " + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryDirectory);
                try
                {
                    await new TestArtifactService().WriteSelectionAsync(temporaryDirectory, new TestExecutorRequest
                    {
                        Checkout = null!,
                        Targets = [target],
                        RunnerOptions = null!
                    }, CancellationToken.None);

                    using var selection = JsonDocument.Parse(File.ReadAllText(Path.Combine(temporaryDirectory, "selection.json")));
                    var selectedTarget = Assert.Single(selection.RootElement.GetProperty("selectedTargets").EnumerateArray().ToArray());
                    Assert.True(selectedTarget.GetProperty("requiresBrowser").GetBoolean());
                }
                finally
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
            }
        }
    }

    [Fact]
    public void AffectedSelectionRetainsBrowserRequirementForRouting()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var affected = AffectedSelectionService.MapChangedPaths(configuration.TestSuites, configuration.CodeAreas,
        [
            "src/server/devops/Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests/SearchLifecycleBrowserTests.cs"
        ]);

        var targets = new TestTargetResolver(configuration.TestSuites).Resolve(new TestTargetRequest
        {
            Suites = ["affected"],
            Framework = "net10.0",
            FrameworkExplicit = true,
            ContinuousIntegration = true
        }, affected.AreasBySuite);

        var target = Assert.Single(targets, static item => item.Suite == "server-orchard-functional");
        Assert.True(target.RequiresBrowser);
        Assert.Contains("server-orchard-functional:Functional", affected.Targets, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrowserWorkloadsHaveExclusiveJobsAndPreserveCoverageAndDocsQualification()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var workflow = File.ReadAllText(Path.Combine(checkout.RepositoryRoot, ".github", "workflows", "build.yml"));
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        string genericLinux = GetJobBlock(workflow, "linux-tests");
        string genericWindows = GetJobBlock(workflow, "windows-tests");
        string orchardLinux = GetJobBlock(workflow, "orchard-browser-linux");
        string orchardWindows = GetJobBlock(workflow, "orchard-browser-windows");
        string coverage = GetJobBlock(workflow, "coverage");
        string docs = GetJobBlock(workflow, "docs-site");

        foreach (string genericJob in new[] { genericLinux, genericWindows })
        {
            Assert.Contains("test all --ci", genericJob, StringComparison.Ordinal);
            Assert.DoesNotContain("server-orchard-functional", genericJob, StringComparison.Ordinal);
            Assert.DoesNotContain("Install Playwright", genericJob, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("playwright.ps1", genericJob, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(LinuxQualificationCondition, GetJobCondition(workflow, "linux-tests"));
        Assert.Equal(LinuxQualificationCondition, GetJobCondition(workflow, "coverage"));
        Assert.Equal(LinuxQualificationCondition, GetJobCondition(workflow, "orchard-browser-linux"));
        Assert.Contains("test server-orchard-functional --ci --framework '${{ matrix.framework }}'", orchardLinux, StringComparison.Ordinal);
        Assert.Contains("test server-orchard-functional --ci --framework '${{ matrix.framework }}'", orchardWindows, StringComparison.Ordinal);
        Assert.Equal(["net10.0", "net11.0"], GetFrameworkMatrix(orchardLinux));
        Assert.Equal(["net10.0", "net11.0"], GetFrameworkMatrix(orchardWindows));
        Assert.Contains("Upload Orchard browser test artefacts", orchardLinux, StringComparison.Ordinal);
        Assert.Contains("Upload Orchard browser test artefacts", orchardWindows, StringComparison.Ordinal);
        Assert.Contains("artifacts/test/runs", orchardLinux, StringComparison.Ordinal);
        Assert.Contains("artifacts/test/runs", orchardWindows, StringComparison.Ordinal);
        Assert.DoesNotContain("Install Playwright Chromium", orchardLinux, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Install Playwright Chromium", orchardWindows, StringComparison.Ordinal);
        Assert.DoesNotContain("check-playwright-runtime.ps1", orchardLinux, StringComparison.Ordinal);
        Assert.DoesNotContain("check-playwright-runtime.ps1", orchardWindows, StringComparison.Ordinal);
        Assert.Contains("git config --global --add safe.directory \"$GITHUB_WORKSPACE\"", orchardLinux, StringComparison.Ordinal);
        Assert.Contains("git -c \"safe.directory=$GITHUB_WORKSPACE\" -C \"$GITHUB_WORKSPACE\" rev-parse --show-toplevel", orchardLinux, StringComparison.Ordinal);

        var assignments = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["server-orchard-functional"] = ["orchard-browser-linux", "orchard-browser-windows"],
            ["docs-site"] = ["docs-site"]
        };
        foreach (string suiteKey in configuration.TestSuites.Where(static pair => pair.Value.RequiresBrowser).Select(static pair => pair.Key))
        {
            Assert.True(assignments.TryGetValue(suiteKey, out string[]? jobNames),
                $"Browser-required suite '{suiteKey}' has no explicit CI job assignment.");
            foreach (string jobName in jobNames!)
            {
                string job = GetJobBlock(workflow, jobName);
                Assert.Contains(suiteKey == "docs-site" ? "docs test" : $"test {suiteKey}", job, StringComparison.Ordinal);
            }
        }

        var packageVersion = Regex.Match(File.ReadAllText(Path.Combine(checkout.RepositoryRoot, "Directory.Packages.props")),
            "<PackageVersion Include=\"Microsoft.Playwright\" Version=\"(?<version>[0-9.]+)\"", RegexOptions.CultureInvariant)
            .Groups["version"].Value;
        Assert.False(string.IsNullOrWhiteSpace(packageVersion));
        foreach (Match image in Regex.Matches(workflow, "mcr\\.microsoft\\.com/playwright/dotnet:v(?<version>[0-9.]+)-noble", RegexOptions.CultureInvariant))
        {
            Assert.Equal(packageVersion, image.Groups["version"].Value);
        }
        Assert.Contains("mcr.microsoft.com/playwright/dotnet:v" + packageVersion + "-noble", orchardLinux, StringComparison.Ordinal);
        Assert.Contains("container:", orchardLinux, StringComparison.Ordinal);
        Assert.Contains("runs-on: ubuntu-24.04", docs, StringComparison.Ordinal);
        Assert.DoesNotContain("container:", docs, StringComparison.Ordinal);
        Assert.DoesNotContain("mcr.microsoft.com/playwright/dotnet:", docs, StringComparison.Ordinal);
        Assert.Contains("name: Verify docs host toolchain", docs, StringComparison.Ordinal);
        Assert.Contains("name: Install Playwright Chromium", docs, StringComparison.Ordinal);
        Assert.Contains("artifacts/bin/Rowles.LeanCorpus.Tests.Docs/release_net10.0", docs, StringComparison.Ordinal);
        Assert.Contains("Test-Path -LiteralPath $playwrightScript -PathType Leaf", docs, StringComparison.Ordinal);
        Assert.Contains("pwsh $playwrightScript install --with-deps chromium", docs, StringComparison.Ordinal);
        Assert.DoesNotContain("check-playwright-runtime.ps1", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("name: Validate Playwright Chromium runtime", workflow, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(checkout.RepositoryRoot, "scripts", "devops", "check-playwright-runtime.ps1")));

        Assert.Contains("needs: coverage", docs, StringComparison.Ordinal);
        Assert.DoesNotContain("needs: linux-tests", coverage, StringComparison.Ordinal);
        Assert.Contains("name: coverage-runs", workflow, StringComparison.Ordinal);
        Assert.Contains("name: leancorpus-docs-site", docs, StringComparison.Ordinal);
        Assert.Contains("./devops docs build", docs, StringComparison.Ordinal);
        Assert.Contains("./devops docs test --framework net10.0 --no-restore", docs, StringComparison.Ordinal);
        Assert.Contains("docs-site-diagnostics", docs, StringComparison.Ordinal);
        Assert.Contains("docs-site-validation", docs, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pull_request", "refs/pull/103/merge", "", true)]
    [InlineData("workflow_dispatch", "refs/heads/issue-97", "", true)]
    [InlineData("push", "refs/heads/main", "ordinary change", true)]
    [InlineData("push", "refs/heads/stabilise/3.1", "ordinary change", true)]
    [InlineData("push", "refs/heads/issue-97", "[deploy] publish docs", true)]
    [InlineData("push", "refs/heads/issue-97", "ordinary change", false)]
    [InlineData("push", "refs/heads/feature/example", "ordinary change", false)]
    public void LinuxQualificationPolicyPreservesRequiredEvents(string eventName, string reference, string message, bool expected)
    {
        bool actual = eventName == "workflow_dispatch" ||
            eventName == "pull_request" ||
            reference == "refs/heads/main" ||
            reference.StartsWith("refs/heads/stabilise/", StringComparison.Ordinal) ||
            message.StartsWith("[deploy]", StringComparison.Ordinal);

        Assert.Equal(expected, actual);
    }

    private static string[] FindPlaywrightProjects(string repositoryRoot)
    {
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        return Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => XDocument.Load(path).Descendants().Any(static element =>
                element.Name.LocalName == "PackageReference" &&
                (string?)element.Attribute("Include") == "Microsoft.Playwright"))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetJobBlock(string workflow, string jobName)
    {
        var match = Regex.Match(workflow,
            $"(?ms)^  {Regex.Escape(jobName)}:\\r?\\n(?<body>.*?)(?=^  [A-Za-z0-9_-]+:|\\z)",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Workflow is missing job '{jobName}'.");
        return match.Groups["body"].Value;
    }

    private static string GetJobCondition(string workflow, string jobName)
    {
        var match = Regex.Match(GetJobBlock(workflow, jobName), "(?m)^    if: (?<condition>.+)$", RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Workflow job '{jobName}' is missing its event qualification condition.");
        return match.Groups["condition"].Value.TrimEnd('\r');
    }

    private static string[] GetFrameworkMatrix(string job)
    {
        var match = Regex.Match(job, "(?m)^        framework: \\[(?<values>[^\\]]+)\\]\\r?$", RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Browser job is missing the full framework matrix.");
        return match.Groups["values"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}

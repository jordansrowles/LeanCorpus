using System.Collections.Immutable;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class TestTargetResolverTests
{
    [Fact]
    public void ResolvesAllInLegacyOrderAndUsesSuiteFrameworkDefaults()
    {
        var resolver = new TestTargetResolver(Suites(
            ("core", Suite(frameworks: ["net10.0", "net11.0"], coverage: true)),
            ("text", Suite(frameworks: ["net10.0", "net11.0"])),
            ("server-core", Suite(frameworks: ["net10.0", "net11.0"], defaultFramework: "net11.0")),
            ("aot", Suite(runner: "AotNative", frameworks: ["net10.0", "net11.0"], expandFrameworks: true)),
            ("docs-site", Suite(frameworks: ["net10.0", "net11.0"], includeInAll: false))));

        var targets = resolver.Resolve(new TestTargetRequest { Suites = ["all"], RuntimeIdentifier = "linux-x64" });

        Assert.Equal(
            ["core/net10.0", "text/net10.0", "server-core/net11.0", "aot/net10.0/linux-x64", "aot/net11.0/linux-x64"],
            targets.Select(static target => target.Key));
        Assert.Equal("AotNative", targets[3].RunnerKind);
    }

    [Fact]
    public void AllowsExplicitSelectionOfSuiteExcludedFromAll()
    {
        var resolver = new TestTargetResolver(Suites(
            ("docs-site", Suite(frameworks: ["net10.0", "net11.0"], includeInAll: false))));

        var target = Assert.Single(resolver.Resolve(new TestTargetRequest { Suites = ["docs-site"] }));

        Assert.Equal("docs-site/net10.0", target.Key);
    }

    [Fact]
    public void ListsSuitesInLegacyOrderAndCanIncludeExplicitOnlySuites()
    {
        var resolver = new TestTargetResolver(Suites(
            ("dataforge", Suite()),
            ("docs-site", Suite(includeInAll: false)),
            ("aot", Suite(runner: "AotNative", expandFrameworks: true)),
            ("text", Suite()),
            ("core", Suite())));

        Assert.Equal(["core", "text", "aot", "dataforge"], resolver.GetOrderedSuiteKeys(includeExplicitOnly: false));
        Assert.Equal(["core", "text", "aot", "dataforge", "docs-site"], resolver.GetOrderedSuiteKeys(includeExplicitOnly: true));
    }

    [Fact]
    public void RejectsUnsupportedExplicitFrameworkInsteadOfSilentlySkippingSuite()
    {
        var resolver = new TestTargetResolver(Suites(("core", Suite(frameworks: ["net10.0"]))));

        var exception = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(new TestTargetRequest
        {
            Suites = ["core"],
            Framework = "net11.0",
            FrameworkExplicit = true
        }));

        Assert.Contains("does not support framework 'net11.0'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CoverageAcrossAllSkipsIneligibleSuitesButExplicitIneligibleSuiteFails()
    {
        var resolver = new TestTargetResolver(Suites(
            ("core", Suite(frameworks: ["net10.0"], coverage: true)),
            ("text", Suite(frameworks: ["net10.0"]))));

        var target = Assert.Single(resolver.Resolve(new TestTargetRequest { Suites = ["all"], CollectCoverage = true }));
        Assert.Equal("core/net10.0", target.Key);

        var exception = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(new TestTargetRequest
        {
            Suites = ["text"],
            CollectCoverage = true
        }));
        Assert.Contains("not eligible for coverage", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvesAffectedAreasForMappedSuitesAndCombinesUserFilters()
    {
        var resolver = new TestTargetResolver(Suites(("core", Suite(frameworks: ["net10.0"]))));
        var affected = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["core"] = ["Search", "Index"]
        };

        var target = Assert.Single(resolver.Resolve(new TestTargetRequest
        {
            Suites = ["affected"],
            Area = "Index,Store",
            Category = "Unit",
            Filter = "FullyQualifiedName~Parser"
        }, affected));

        Assert.Equal("core/net10.0", target.Key);
        Assert.Equal("(Area=Index|Area=Search)&(Area=Index|Area=Store)&(Category=Unit)&FullyQualifiedName~Parser", target.Filter);
        Assert.Equal(["Index", "Search"], target.Areas);
    }

    private static IReadOnlyDictionary<string, TestSuiteConfiguration> Suites(
        params (string Key, TestSuiteConfiguration Configuration)[] suites)
        => suites.ToDictionary(static item => item.Key, static item => item.Configuration, StringComparer.OrdinalIgnoreCase);

    private static TestSuiteConfiguration Suite(
        string runner = "Mtp",
        ImmutableArray<string> frameworks = default,
        string? defaultFramework = null,
        bool coverage = false,
        bool? includeInAll = null,
        bool expandFrameworks = false)
        => new(
            Name: "Fixture suite",
            Project: $"src/devops/{Guid.NewGuid():N}.csproj",
            Runner: runner,
            Frameworks: frameworks.IsDefault ? ["net10.0", "net11.0"] : frameworks,
            DefaultFramework: defaultFramework,
            Coverage: coverage,
            Capabilities: ["Trx"],
            IncludeInAll: includeInAll,
            ExpandFrameworksByDefault: expandFrameworks,
            DefaultFilter: null);
}

using System.Collections.Immutable;
using Rowles.LeanCorpus.DevOps.Configuration;

namespace Rowles.LeanCorpus.DevOps.Tests.Configuration;

public sealed class BenchmarkManifestValidatorTests
{
    [Fact]
    public void AcceptsMatchingProjectSuiteClassAndInclusionManifests()
    {
        var configuration = CreateConfiguration();

        BenchmarkManifestValidator.Validate(configuration, CreateManifests());
    }

    [Fact]
    public void RejectsBenchmarkGroupClassMissingFromItsOwningHost()
    {
        var configuration = CreateConfiguration();
        var manifests = CreateManifests();
        manifests["text"] = manifests["text"].Replace("TextParityBenchmarks", "RenamedTextBenchmarks", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() => BenchmarkManifestValidator.Validate(configuration, manifests));

        Assert.Contains("benchmark-groups.json $.data.text-group.Benchmarks[0]", error.Message, StringComparison.Ordinal);
        Assert.Contains("TextParityBenchmarks", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDiscoveredBenchmarkClassWithoutSuiteRegistration()
    {
        var configuration = CreateConfiguration();
        var manifests = CreateManifests();
        manifests["core"] = manifests["core"].Replace(
            "\"benchmarkClasses\":[",
            "\"discoveredBenchmarkClasses\":[{\"type\":\"Tests.QueryBenchmarks\"},{\"type\":\"Tests.OrphanApproximationBenchmarks\"},{\"type\":\"Tests.OrphanFlushBenchmarks\"}],\"benchmarkClasses\":[",
            StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() => BenchmarkManifestValidator.Validate(configuration, manifests));

        Assert.Contains("OrphanApproximationBenchmarks", error.Message, StringComparison.Ordinal);
        Assert.Contains("OrphanFlushBenchmarks", error.Message, StringComparison.Ordinal);
        Assert.Contains("suite registration", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsSuiteAliasDriftBetweenConfigurationAndCoreHost()
    {
        var configuration = CreateConfiguration();
        configuration = configuration with
        {
            BenchmarkSuites = configuration.BenchmarkSuites.SetItem("query", configuration.BenchmarkSuites["query"] with
            {
                Aliases = ["term-query"]
            })
        };

        var error = Assert.Throws<InvalidDataException>(() => BenchmarkManifestValidator.Validate(configuration, CreateManifests()));

        Assert.Contains("Suite aliases for 'query' differ", error.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> CreateManifests()
        => new(StringComparer.Ordinal)
        {
            ["core"] = """
                {"schemaVersion":1,"project":"core","suites":[
                  {"id":"all","aliases":[],"isSelector":true},
                  {"id":"all-with-explicit","aliases":[],"isSelector":true},
                  {"id":"explicit","aliases":[],"isSelector":true},
                  {"id":"query","aliases":[],"isSelector":false}],
                 "benchmarkClasses":[{"type":"Tests.QueryBenchmarks","suiteIds":["query"],"includeInAll":true,"includeWithExplicit":false}]}
                """,
            ["text"] = """
                {"schemaVersion":1,"project":"text","benchmarkClasses":[{"type":"Tests.TextParityBenchmarks"}]}
                """,
            ["compression"] = """
                {"schemaVersion":1,"project":"compression","benchmarkClasses":[{"type":"Tests.CompressionBenchmarks"}]}
                """
        };

    private static DevOpsConfiguration CreateConfiguration()
    {
        var projects = ImmutableDictionary.CreateRange(StringComparer.Ordinal,
        [
            KeyValuePair.Create("core", new BenchmarkProjectConfiguration("Core", "src/core.csproj", ["net10.0"], ["Wikipedia", "Groups"], true, true)),
            KeyValuePair.Create("text", new BenchmarkProjectConfiguration("Text", "src/text.csproj", ["net10.0"], ["Groups"], true, true)),
            KeyValuePair.Create("compression", new BenchmarkProjectConfiguration("Compression", "src/compression.csproj", ["net10.0"], ["Independent"], true, true))
        ]);
        var suites = ImmutableDictionary.CreateRange(StringComparer.Ordinal,
        [
            KeyValuePair.Create("all", Suite("core", selector: true)),
            KeyValuePair.Create("all-with-explicit", Suite("core", selector: true)),
            KeyValuePair.Create("explicit", Suite("core", selector: true)),
            KeyValuePair.Create("query", Suite("core")),
            KeyValuePair.Create("analysis-parity", Suite("text", benchmarkClasses: ["TextParityBenchmarks"]))
        ]);
        var groups = ImmutableDictionary.CreateRange(StringComparer.Ordinal,
        [
            KeyValuePair.Create("text-group", new BenchmarkGroupConfiguration("Search", ["src/**"], "text", ["TextParityBenchmarks"]))
        ]);
        return new DevOpsConfiguration(
            ImmutableDictionary.Create<string, TestSuiteConfiguration>(StringComparer.Ordinal),
            ImmutableDictionary.Create<string, CodeAreaConfiguration>(StringComparer.Ordinal),
            projects,
            suites,
            ImmutableDictionary.Create<string, BenchmarkStrategyConfiguration>(StringComparer.Ordinal),
            ImmutableDictionary.Create<string, ImmutableArray<string>>(StringComparer.Ordinal),
            groups);
    }

    private static BenchmarkSuiteConfiguration Suite(string project, bool selector = false, ImmutableArray<string> benchmarkClasses = default)
        => new("Description", "Display", project, benchmarkClasses.IsDefault ? [] : benchmarkClasses, [], [], [], false, true, false,
            false, selector, null, null);
}

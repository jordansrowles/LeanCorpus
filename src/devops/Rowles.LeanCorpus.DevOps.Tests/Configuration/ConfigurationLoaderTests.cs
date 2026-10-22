using System.Text.Json.Nodes;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Tests.Configuration;

public sealed class ConfigurationLoaderTests
{
    [Fact]
    public void LoadsSixVersionedManagedConfigurationSetsAndResolvesReferences()
    {
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        Assert.NotEmpty(configuration.TestSuites);
        Assert.NotEmpty(configuration.CodeAreas);
        Assert.NotEmpty(configuration.BenchmarkSuites);
        Assert.NotEmpty(configuration.BenchmarkStrategies);
        Assert.NotEmpty(configuration.BenchmarkDatasets);
        Assert.NotEmpty(configuration.BenchmarkGroups);
        Assert.Contains("core", configuration.TestSuites.Keys);
    }

    [Fact]
    public void LoadsBritishSerialisationSelectorsAndRejectsAmericanAliases()
    {
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        Assert.Contains("serialisation", configuration.CodeAreas.Keys);
        Assert.False(configuration.CodeAreas.ContainsKey("serialization"));
        var codeArea = configuration.CodeAreas["serialisation"];
        Assert.Contains("src/core/Rowles.LeanCorpus/Serialisation/**", codeArea.Globs);
        Assert.Equal("core:Serialisation", Assert.Single(codeArea.Targets));

        Assert.Contains("Serialisation", configuration.BenchmarkGroups.Keys);
        Assert.False(configuration.BenchmarkGroups.ContainsKey("Serialization"));
        var benchmarkGroup = configuration.BenchmarkGroups["Serialisation"];
        Assert.Equal("Serialisation", benchmarkGroup.Area);
        Assert.Contains("src/core/Rowles.LeanCorpus/Serialisation/**", benchmarkGroup.Globs);

        var legacyCodeAreas = File.ReadAllText(Path.Combine(checkout.RepositoryRoot, "scripts/devops/config/code-areas.psd1"));
        Assert.Contains("'serialisation'", legacyCodeAreas, StringComparison.Ordinal);
        Assert.Contains("'core:Serialisation'", legacyCodeAreas, StringComparison.Ordinal);
        Assert.DoesNotContain("'serialization'", legacyCodeAreas, StringComparison.Ordinal);

        var legacyBenchmarkGroups = File.ReadAllText(Path.Combine(checkout.RepositoryRoot, "scripts/devops/config/benchmark-groups.psd1"));
        Assert.Contains("'Serialisation' = @{ Area = 'Serialisation'", legacyBenchmarkGroups, StringComparison.Ordinal);
        Assert.DoesNotContain("'Serialization' = @{ Area = 'Serialization'", legacyBenchmarkGroups, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownPropertiesAndDuplicateJsonKeys()
    {
        using var root = TemporaryConfiguration.Create();
        var testSuites = root.ConfigPath("test-suites.json");
        var document = JsonNode.Parse(File.ReadAllText(testSuites))!;
        document["data"]!["core"]!["Unexpected"] = true;
        File.WriteAllText(testSuites, document.ToJsonString());

        var unknownError = Assert.Throws<InvalidDataException>(() => root.Load());
        Assert.Contains("unknown property 'Unexpected'", unknownError.Message, StringComparison.Ordinal);

        root.Restore();
        File.WriteAllText(testSuites, "{\"schemaVersion\":1,\"schemaVersion\":1,\"data\":{\"x\":\"value\"}}");
        var duplicateError = Assert.Throws<InvalidDataException>(() => root.Load());
        Assert.Contains("duplicate property 'schemaVersion'", duplicateError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnsupportedSchemaVersionAndInvalidSuiteReference()
    {
        using var root = TemporaryConfiguration.Create();
        var testSuites = root.ConfigPath("test-suites.json");
        var document = JsonNode.Parse(File.ReadAllText(testSuites))!;
        document["schemaVersion"] = 99;
        File.WriteAllText(testSuites, document.ToJsonString());

        var versionError = Assert.Throws<InvalidDataException>(() => root.Load());
        Assert.Contains("unsupported version 99", versionError.Message, StringComparison.Ordinal);

        root.Restore();
        var codeAreasPath = root.ConfigPath("code-areas.json");
        var codeAreas = JsonNode.Parse(File.ReadAllText(codeAreasPath))!;
        codeAreas["data"]!["store"]!["Targets"]![0] = "missing:Index";
        File.WriteAllText(codeAreasPath, codeAreas.ToJsonString());

        var referenceError = Assert.Throws<InvalidDataException>(() => root.Load());
        Assert.Contains("unknown test suite 'missing'", referenceError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsInvalidDatasetReferencesAndProjectPathEscape()
    {
        using var root = TemporaryConfiguration.Create();
        var datasetsPath = root.ConfigPath("benchmark-datasets.json");
        var datasets = JsonNode.Parse(File.ReadAllText(datasetsPath))!;
        datasets["data"]!["wikipedia"]![0] = "nonexistent-suite";
        File.WriteAllText(datasetsPath, datasets.ToJsonString());

        var datasetError = Assert.Throws<InvalidDataException>(() => root.Load());
        Assert.Contains("unknown benchmark suite reference", datasetError.Message, StringComparison.Ordinal);

        root.Restore();
        var testSuitesPath = root.ConfigPath("test-suites.json");
        var testSuites = JsonNode.Parse(File.ReadAllText(testSuitesPath))!;
        testSuites["data"]!["core"]!["Project"] = "../outside.csproj";
        File.WriteAllText(testSuitesPath, testSuites.ToJsonString());

        var pathError = Assert.Throws<InvalidDataException>(() => root.Load());
        Assert.Contains("existing repository project file", pathError.Message, StringComparison.Ordinal);
    }

    private sealed class TemporaryConfiguration : IDisposable
    {
        private readonly string _sourceRoot;

        private TemporaryConfiguration(string path, string sourceRoot)
        {
            Path = path;
            _sourceRoot = sourceRoot;
        }

        public string Path { get; }

        public string ConfigPath(string fileName) => System.IO.Path.Combine(Path, ".config", "devops", fileName);

        public static TemporaryConfiguration Create()
        {
            var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DevOps config " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(path, ".config", "devops"));
            var sourceRoot = System.IO.Path.Combine(checkout.RepositoryRoot, ".config", "devops");
            foreach (var source in Directory.EnumerateFiles(sourceRoot, "*.json"))
            {
                File.Copy(source, System.IO.Path.Combine(path, ".config", "devops", System.IO.Path.GetFileName(source)));
            }

            return new TemporaryConfiguration(path, sourceRoot);
        }

        public ConfigurationLoader Loader() => new(Path, _ => true);

        public DevOpsConfiguration Load() => Loader().Load();

        public void Restore()
        {
            foreach (var source in Directory.EnumerateFiles(_sourceRoot, "*.json"))
            {
                File.Copy(source, System.IO.Path.Combine(Path, ".config", "devops", System.IO.Path.GetFileName(source)), overwrite: true);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

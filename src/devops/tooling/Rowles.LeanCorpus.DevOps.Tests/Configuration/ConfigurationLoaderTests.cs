using System.Text.Json.Nodes;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Tests.Configuration;

public sealed class ConfigurationLoaderTests
{
    [Fact]
    public void FuzzyParityFamiliesRecordTheMatchedClassicLevenshteinParameters()
    {
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var manifestPath = Path.Combine(checkout.RepositoryRoot, ".config", "devops", "benchmark-cases.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var families = document.RootElement.GetProperty("caseFamilies").EnumerateArray()
            .Where(static item => item.GetProperty("benchmarkClass").GetString() == "FuzzyQueryBenchmarks")
            .ToDictionary(static item => item.GetProperty("id").GetString()!, StringComparer.Ordinal);

        var expectedEditDistances = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fuzzy-parity-zero-edit-leancorpus"] = "0",
            ["fuzzy-parity-zero-edit-lucenenet"] = "0",
            ["fuzzy-parity-edit1-leancorpus"] = "1",
            ["fuzzy-parity-edit1-lucenenet"] = "1",
            ["fuzzy-parity-edit2-leancorpus"] = "2",
            ["fuzzy-parity-edit2-lucenenet"] = "2"
        };

        foreach (var (id, maxEdits) in expectedEditDistances)
        {
            var parameters = families[id].GetProperty("semanticParameters");
            Assert.Equal(maxEdits, parameters.GetProperty("maxEdits").GetString());
            Assert.Equal("50", parameters.GetProperty("maxExpansions").GetString());
            Assert.Equal("0", parameters.GetProperty("prefixLength").GetString());
            Assert.Equal("false", parameters.GetProperty("transpositions").GetString());
        }
    }

    [Fact]
    public void FuzzyCacheRegistersUncachedNegativeCaseWithoutComparisonGroup()
    {
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var benchmarkCase = configuration.BenchmarkCases.Single(item => item.Id == "fuzzy-cache-negative-uncached");

        Assert.Equal("FuzzyExpansionCacheBenchmarks", benchmarkCase.BenchmarkClass);
        Assert.Equal(["UncachedNegativeExpansionCache"], benchmarkCase.BenchmarkMethods);
        Assert.Equal("UncachedExpansion", benchmarkCase.CachePolicy);
        Assert.Null(benchmarkCase.ComparisonGroup);
        Assert.Equal("Unqualified", benchmarkCase.Classification);
        Assert.Contains("dictionary", benchmarkCase.QualityProof, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CapacityOneAsyncStressIsExplicitAndHasIndependentDocumentBudgets()
    {
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var suite = configuration.BenchmarkSuites["async-index-stress"];
        Assert.True(suite.ExplicitOnly);
        Assert.Equal(["AsyncIndexingCapacityOneStressBenchmarks"], suite.BenchmarkClasses);

        var bounded = configuration.BenchmarkStrategies["async-stress"];
        Assert.Equal(1_000, bounded.DocCount);
        Assert.Equal(30, bounded.AggregateTimeoutMinutes);
        Assert.Equal(30, bounded.ProjectTimeoutMinutes);

        var deep = configuration.BenchmarkStrategies["async-stress-deep"];
        Assert.Equal(100_000, deep.DocCount);
        Assert.Equal(1_440, deep.AggregateTimeoutMinutes);
        Assert.Equal(1_440, deep.ProjectTimeoutMinutes);
    }

    [Fact]
    public void LoadsVersionedManagedConfigurationSetsAndResolvesReferences()
    {
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        Assert.NotEmpty(configuration.TestSuites);
        Assert.NotEmpty(configuration.CodeAreas);
        Assert.NotEmpty(configuration.BenchmarkProjects);
        Assert.NotEmpty(configuration.BenchmarkSuites);
        Assert.NotEmpty(configuration.BenchmarkStrategies);
        Assert.NotEmpty(configuration.BenchmarkDatasets);
        Assert.NotEmpty(configuration.BenchmarkGroups);
        Assert.NotEmpty(configuration.BenchmarkCases);
        Assert.Contains(configuration.BenchmarkCases, benchmarkCase => benchmarkCase.Id == "fuzzy-negative-no-hit-leancorpus");
        Assert.Contains(configuration.BenchmarkCases, benchmarkCase => benchmarkCase.Id == "fuzzy-parity-zero-edit-leancorpus");
        Assert.Contains(configuration.BenchmarkCases, benchmarkCase => benchmarkCase.Id == "fuzzy-parity-zero-edit-lucenenet");
        Assert.Contains(configuration.BenchmarkCases, benchmarkCase => benchmarkCase.Id == "fuzzy-cache-eviction-cycle");
        Assert.Contains(configuration.BenchmarkCases, benchmarkCase => benchmarkCase.Id == "fuzzy-cache-negative-uncached");
        var suggesterFamilies = configuration.BenchmarkCases
            .Where(benchmarkCase => benchmarkCase.BenchmarkClass == "SuggesterBenchmarks")
            .ToArray();
        Assert.Equal(3, suggesterFamilies.Length);
        Assert.All(suggesterFamilies, benchmarkCase =>
        {
            Assert.Equal("Directional", benchmarkCase.Classification);
            Assert.Null(benchmarkCase.ComparisonGroup);
            Assert.Equal("bval-15-suggester-quality-ground-truth", benchmarkCase.SemanticParameters["qualityFixture"]);
        });
        var zeroEditFamily = configuration.BenchmarkCases.Single(benchmarkCase => benchmarkCase.Id == "fuzzy-parity-zero-edit-leancorpus");
        Assert.Equal("0", zeroEditFamily.SemanticParameters["maxEdits"]);
        Assert.Equal("50", zeroEditFamily.SemanticParameters["maxExpansions"]);
        Assert.Equal("0", zeroEditFamily.SemanticParameters["prefixLength"]);
        Assert.Equal("false", zeroEditFamily.SemanticParameters["transpositions"]);
        Assert.Contains("maxEdits=0", zeroEditFamily.QualityProof, StringComparison.Ordinal);
        Assert.Contains("maxExpansions=50", zeroEditFamily.QualityProof, StringComparison.Ordinal);
        Assert.Contains("prefixLength=0", zeroEditFamily.QualityProof, StringComparison.Ordinal);
        Assert.Contains("transpositions=false", zeroEditFamily.QualityProof, StringComparison.Ordinal);
        Assert.Equal(["FuzzyQueryBenchmarks"], configuration.BenchmarkSuites["fuzzy-parity"].BenchmarkClasses);
        Assert.Equal(["FuzzyExpansionCacheBenchmarks"], configuration.BenchmarkSuites["fuzzy-cache"].BenchmarkClasses);
        Assert.Equal(["fuzzy"], configuration.BenchmarkSuites["fuzzy-parity"].Aliases);
        Assert.Equal(15, configuration.BenchmarkCorrectnessAudit.Gates.Length);
        Assert.All(configuration.BenchmarkCorrectnessAudit.Gates,
            gate => Assert.Equal("Quarantined", gate.Status));
        Assert.All(configuration.BenchmarkCorrectnessAudit.Gates,
            gate => Assert.Equal("ExecutedPassing", gate.FixtureState));
        Assert.NotEmpty(configuration.BenchmarkCaseExclusions);
        Assert.All(configuration.BenchmarkCaseExclusions,
            exclusion => Assert.True(exclusion.ExpiresOn >= DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.Equal("synthetic", configuration.DefaultBenchmarkDataset);
        Assert.Contains("core", configuration.TestSuites.Keys);
    }

    [Fact]
    public void BenchmarkStrategiesDeclareIndependentAggregateBuildAndProjectBudgets()
    {
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var strategiesPath = Path.Combine(checkout.RepositoryRoot, ".config", "devops", "benchmark-strategies.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(strategiesPath));
        var strategies = document.RootElement.GetProperty("data");
        var exhaustive = strategies.GetProperty("exhaustive");

        Assert.Equal(1440, exhaustive.GetProperty("AggregateTimeoutMinutes").GetInt32());
        Assert.InRange(exhaustive.GetProperty("BuildTimeoutMinutes").GetInt32(), 1, 40_000);
        Assert.InRange(exhaustive.GetProperty("ProjectTimeoutMinutes").GetInt32(), 1, 40_000);
        Assert.True(strategies.GetProperty("quick-compare").GetProperty("AggregateTimeoutMinutes").GetInt32() < 1440);

        var loaded = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        Assert.Equal(1440, loaded.BenchmarkStrategies["exhaustive"].AggregateTimeoutMinutes);
    }

    [Fact]
    public void RejectsBenchmarkStrategyTimeoutOutsideSupportedTimerRange()
    {
        using var root = TemporaryConfiguration.Create();
        var path = root.ConfigPath("benchmark-strategies.json");
        var document = JsonNode.Parse(File.ReadAllText(path))!;
        document["data"]!["exhaustive"]!["AggregateTimeoutMinutes"] = 0;
        File.WriteAllText(path, document.ToJsonString());

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("AggregateTimeoutMinutes", error.Message, StringComparison.Ordinal);
        Assert.Contains("between 1 and 40000", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsComparisonGroupWithDifferentSemanticParameters()
    {
        using var root = TemporaryConfiguration.Create();
        var path = root.ConfigPath("benchmark-cases.json");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!;
        var luceneFamily = manifest["caseFamilies"]!.AsArray().Single(item =>
            item!["id"]!.GetValue<string>() == "fuzzy-parity-edit1-lucenenet");
        luceneFamily!["semanticParameters"]!["maxEdits"] = "2";
        File.WriteAllText(path, manifest.ToJsonString());

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("comparison group 'fuzzy-parity-edit1-20k' has incompatible semantic parameters", error.Message, StringComparison.Ordinal);
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
    public void RejectsBrowserRequiredSuiteInGenericAllSelection()
    {
        using var root = TemporaryConfiguration.Create();
        var testSuitesPath = root.ConfigPath("test-suites.json");
        var testSuites = JsonNode.Parse(File.ReadAllText(testSuitesPath))!;
        testSuites["data"]!["server-orchard-functional"]!["IncludeInAll"] = true;
        File.WriteAllText(testSuitesPath, testSuites.ToJsonString());

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("browser-required suites must be excluded from the generic all selector", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsIncompleteBenchmarkCorrectnessAudit()
    {
        using var root = TemporaryConfiguration.Create();
        File.WriteAllText(root.ConfigPath("benchmark-correctness-audit.json"), """
            {
              "$schema": "benchmark-correctness-audit.schema.json",
              "schemaVersion": 1,
              "specRevision": 3,
              "auditedBranch": "issue-97",
              "auditedCommit": "940334da5e5132e4a2d8378e21e8eedc1771ca95",
              "auditedTreeDirty": true,
              "auditState": "PreRemediation",
              "gates": []
            }
            """);

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("benchmark-correctness-audit.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("15", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsBenchmarkCorrectnessAuditWhenFixtureFingerprintDiffers()
    {
        using var root = TemporaryConfiguration.Create();
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var sourceAuditPath = Path.Combine(checkout.RepositoryRoot, ".config", "devops", "benchmark-correctness-audit.json");
        var audit = JsonNode.Parse(File.ReadAllText(sourceAuditPath))!;
        audit["gates"]![0]!["fixtureSha256"] = new string('0', 64);
        File.WriteAllText(root.ConfigPath("benchmark-correctness-audit.json"), audit.ToJsonString());

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("fixture SHA-256", error.Message, StringComparison.Ordinal);
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
    public void RejectsParityBenchmarkFamilyWithoutComparisonGroup()
    {
        using var root = TemporaryConfiguration.Create();
        File.WriteAllText(root.ConfigPath("benchmark-cases.json"), """
            {
              "schemaVersion": 1,
              "exclusions": [],
              "caseFamilies": [
                {
                  "id": "fuzzy-negative-uncached",
                  "project": "core",
                  "benchmarkClass": "FuzzyQueryBenchmarks",
                  "benchmarkMethods": ["LeanCorpus_Negative_NoHits"],
                  "engineId": "leancorpus",
                  "category": "Search",
                  "operation": "FuzzySearch",
                  "datasetProfile": "synthetic-search-v1",
                  "workloadId": "fuzzy-levenshtein-v1",
                  "algorithmFamily": "FuzzyLevenshtein",
                  "cachePolicy": "UncachedExpansion",
                  "lifecyclePolicy": "warm-open",
                  "comparisonGroup": null,
                  "classification": "StrictParity",
                  "resultContract": "zero-expansions-and-zero-hits",
                  "qualityProof": "The preflight must prove zero expanded terms and zero hits for both engines.",
                  "metrics": [{ "name": "Mean", "unit": "ns", "role": "primary" }],
                  "estimatedBudget": "short"
                }
              ]
            }
            """);

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("benchmark-cases.json", error.Message, StringComparison.Ordinal);
        Assert.True(error.Message.Contains("StrictParity requires a comparisonGroup", StringComparison.Ordinal), error.Message);
    }

    [Fact]
    public void RejectsBenchmarkCaseFamilyForUnknownProject()
    {
        using var root = TemporaryConfiguration.Create();
        File.WriteAllText(root.ConfigPath("benchmark-cases.json"), """
            {
              "schemaVersion": 1,
              "exclusions": [],
              "caseFamilies": [
                {
                  "id": "orphan-case",
                  "project": "missing",
                  "benchmarkClass": "MissingBenchmarks",
                  "benchmarkMethods": ["Run"],
                  "engineId": "leancorpus",
                  "category": "Search",
                  "operation": "Search",
                  "datasetProfile": "synthetic-search-v1",
                  "workloadId": "search-v1",
                  "algorithmFamily": "TermQuery",
                  "cachePolicy": "NotApplicable",
                  "lifecyclePolicy": "NotApplicable",
                  "comparisonGroup": null,
                  "classification": "Unqualified",
                  "resultContract": "stable-logical-results",
                  "qualityProof": "No qualification evidence is recorded.",
                  "metrics": [{ "name": "Mean", "unit": "ns", "role": "primary" }],
                  "estimatedBudget": "short"
                }
              ]
            }
            """);

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("benchmark-cases.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown benchmark project 'missing'", error.Message, StringComparison.Ordinal);
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

    [Fact]
    public void LoadsLegacySuiteStringValuesAndOptionalBenchmarkDescriptors()
    {
        using var root = TemporaryConfiguration.Create();
        var suitesPath = root.ConfigPath("benchmark-suites.json");
        var suites = JsonNode.Parse(File.ReadAllText(suitesPath))!;
        suites["suiteMetadata"] = JsonNode.Parse("""
            {
              "query": {
                "DisplayName": "Term Query",
                "Project": "core",
                "Aliases": ["term-query"],
                "Frameworks": ["net10.0", "net11.0"],
                "ExplicitOnly": false,
                "ControlledCorpusOnly": false,
                "CorpusOnly": false,
                "Datasets": ["wikipedia"]
              }
            }
            """);
        File.WriteAllText(suitesPath, suites.ToJsonString());

        File.WriteAllText(root.ConfigPath("benchmark-projects.json"), """
            {
              "schemaVersion": 1,
              "data": {
                "core": {
                  "Project": "src/devops/benchmarks/Rowles.LeanCorpus.Benchmarks/Rowles.LeanCorpus.Benchmarks.csproj",
                  "Frameworks": ["net10.0", "net11.0"],
                  "Capabilities": ["Groups", "Wikipedia"],
                  "IncludeInAll": true,
                  "IncludeWithExplicit": true
                },
                "text": {
                  "Project": "src/devops/benchmarks/Rowles.Text.Benchmarks/Rowles.Text.Benchmarks.csproj",
                  "Frameworks": ["net10.0", "net11.0"],
                  "Capabilities": ["Groups"],
                  "IncludeInAll": true,
                  "IncludeWithExplicit": true
                },
                "compression": {
                  "Project": "src/devops/benchmarks/Rowles.LeanCorpus.Benchmarks.Compression/Rowles.LeanCorpus.Benchmarks.Compression.csproj",
                  "Frameworks": ["net10.0", "net11.0"],
                  "Capabilities": ["Independent"],
                  "IncludeInAll": true,
                  "IncludeWithExplicit": true
                }
              }
            }
            """);

        var configuration = root.Load();
        var configurationType = configuration.GetType();
        var projectsProperty = configurationType.GetProperty("BenchmarkProjects");
        Assert.NotNull(projectsProperty);
        var projects = projectsProperty.GetValue(configuration)!;
        var coreProject = projects.GetType().GetProperty("Item")!.GetValue(projects, ["core"]);
        Assert.Equal("src/devops/benchmarks/Rowles.LeanCorpus.Benchmarks/Rowles.LeanCorpus.Benchmarks.csproj",
            coreProject!.GetType().GetProperty("Project")!.GetValue(coreProject));

        var suitesProperty = configurationType.GetProperty("BenchmarkSuites");
        Assert.NotNull(suitesProperty);
        var suiteMap = suitesProperty.GetValue(configuration)!;
        var querySuite = suiteMap.GetType().GetProperty("Item")!.GetValue(suiteMap, ["query"]);
        Assert.Equal("TermQueryBenchmarks", querySuite!.GetType().GetProperty("Description")!.GetValue(querySuite));
        Assert.Equal("Term Query", querySuite.GetType().GetProperty("DisplayName")!.GetValue(querySuite));
    }

    [Fact]
    public void RejectsCaseInsensitiveDuplicateConfigurationIdsWithSourceLocation()
    {
        using var root = TemporaryConfiguration.Create();
        File.WriteAllText(root.ConfigPath("benchmark-projects.json"), """
            {"schemaVersion":1,"data":{"core":{},"Core":{}}}
            """);

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("benchmark-projects.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("$.data", error.Message, StringComparison.Ordinal);
        Assert.Contains("duplicate property 'Core'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownBenchmarkProjectReferenceWithSourceLocation()
    {
        using var root = TemporaryConfiguration.Create();
        var suitesPath = root.ConfigPath("benchmark-suites.json");
        var suites = JsonNode.Parse(File.ReadAllText(suitesPath))!;
        suites["suiteMetadata"] = JsonNode.Parse("""
            {"query":{"DisplayName":"Term Query","Project":"missing"}}
            """);
        File.WriteAllText(suitesPath, suites.ToJsonString());

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("benchmark-suites.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("$.suiteMetadata.query.Project", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown benchmark project 'missing'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsBenchmarkProjectPathEscape()
    {
        using var root = TemporaryConfiguration.Create();
        var path = root.ConfigPath("benchmark-projects.json");
        var projects = JsonNode.Parse(File.ReadAllText(path))!;
        projects["data"]!["core"]!["Project"] = "../outside.csproj";
        File.WriteAllText(path, projects.ToJsonString());

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("benchmark-projects.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("$.data.core.Project", error.Message, StringComparison.Ordinal);
        Assert.Contains("relative repository .csproj path", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnsupportedBenchmarkFramework()
    {
        using var root = TemporaryConfiguration.Create();
        var path = root.ConfigPath("benchmark-projects.json");
        var projects = JsonNode.Parse(File.ReadAllText(path))!;
        projects["data"]!["core"]!["Frameworks"]![0] = "net9.0";
        File.WriteAllText(path, projects.ToJsonString());

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("benchmark-projects.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("$.data.core.Frameworks", error.Message, StringComparison.Ordinal);
        Assert.Contains("unsupported benchmark framework", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnsupportedMtpRunnerCapability()
    {
        using var root = TemporaryConfiguration.Create();
        var path = root.ConfigPath("test-suites.json");
        var suites = JsonNode.Parse(File.ReadAllText(path))!;
        suites["data"]!["core"]!["Capabilities"]![1] = "HangDunp";
        File.WriteAllText(path, suites.ToJsonString());

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("test-suites.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("$.data.core.Capabilities", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown or duplicate capability", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDuplicateSuiteAlias()
    {
        using var root = TemporaryConfiguration.Create();
        var path = root.ConfigPath("benchmark-suites.json");
        var suites = JsonNode.Parse(File.ReadAllText(path))!;
        suites["suiteMetadata"] = JsonNode.Parse("""
            {
              "query":{"DisplayName":"Term Query","Project":"core","Aliases":["shared"]},
              "hnsw":{"DisplayName":"HNSW","Project":"core","Aliases":["shared"]}
            }
            """);
        File.WriteAllText(path, suites.ToJsonString());

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("benchmark-suites.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("$.suiteMetadata.query.Aliases[0]", error.Message, StringComparison.Ordinal);
        Assert.Contains("duplicate benchmark suite alias 'shared'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsContradictoryExplicitOnlyPolicy()
    {
        using var root = TemporaryConfiguration.Create();
        var path = root.ConfigPath("benchmark-suites.json");
        var suites = JsonNode.Parse(File.ReadAllText(path))!;
        suites["suiteMetadata"] = JsonNode.Parse("""
            {"query":{"ExplicitOnly":true,"IncludeInAll":true}}
            """);
        File.WriteAllText(path, suites.ToJsonString());

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("benchmark-suites.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("$.suiteMetadata.query.IncludeInAll", error.Message, StringComparison.Ordinal);
        Assert.Contains("explicit-only suite cannot be included", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownBenchmarkGroupProjectWithSourceLocation()
    {
        using var root = TemporaryConfiguration.Create();
        var path = root.ConfigPath("benchmark-groups.json");
        var groups = JsonNode.Parse(File.ReadAllText(path))!;
        groups["data"]!["Parser"]!["Project"] = "missing";
        File.WriteAllText(path, groups.ToJsonString());

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("benchmark-groups.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("$.data.Parser.Project", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown benchmark project 'missing'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsObsoleteSuiteRegistration()
    {
        using var root = TemporaryConfiguration.Create();
        var path = root.ConfigPath("benchmark-suites.json");
        var suites = JsonNode.Parse(File.ReadAllText(path))!;
        suites["suiteMetadata"] = JsonNode.Parse("""
            {"removed-suite":{"DisplayName":"Removed","Project":"core"}}
            """);
        File.WriteAllText(path, suites.ToJsonString());

        var error = Assert.Throws<InvalidDataException>(() => root.Load());

        Assert.Contains("benchmark-suites.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("$.suiteMetadata.removed-suite", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown benchmark suite 'removed-suite'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatesCodeAreaTargetsAgainstTheDeclaredTestAreaTaxonomy()
    {
        using var root = TemporaryConfiguration.Create();
        var suitesPath = root.ConfigPath("test-suites.json");
        var suites = JsonNode.Parse(File.ReadAllText(suitesPath))!;
        suites["areas"]!.AsArray().Add("CustomArea");
        File.WriteAllText(suitesPath, suites.ToJsonString());

        var codeAreasPath = root.ConfigPath("code-areas.json");
        var codeAreas = JsonNode.Parse(File.ReadAllText(codeAreasPath))!;
        codeAreas["data"]!["store"]!["Targets"]![0] = "core:CustomArea";
        File.WriteAllText(codeAreasPath, codeAreas.ToJsonString());

        var configuration = root.Load();

        Assert.Contains("core", configuration.TestSuites.Keys);
    }

    [Fact]
    public void ResolvesAllSuitesByConfiguredOrderBeforeDeterministicFallback()
    {
        using var root = TemporaryConfiguration.Create();
        var suitesPath = root.ConfigPath("test-suites.json");
        var suites = JsonNode.Parse(File.ReadAllText(suitesPath))!;
        suites["data"]!["core"]!["Order"] = 40;
        suites["data"]!["text"]!["Order"] = 10;
        suites["data"]!["architecture"]!["Order"] = 20;
        File.WriteAllText(suitesPath, suites.ToJsonString());

        var configuration = root.Load();
        var resolver = new Rowles.LeanCorpus.DevOps.Testing.TestTargetResolver(configuration.TestSuites);

        var ordered = resolver.GetOrderedSuiteKeys(includeExplicitOnly: false);
        Assert.Equal(new[] { "text", "architecture", "sourcegen", "core" }, ordered.Take(4));
        Assert.Equal(new[] { "aot", "dataforge", "server-orchard" }, ordered.TakeLast(3));
        Assert.DoesNotContain("docs-site", ordered);
    }

    [Fact]
    public void RejectsDuplicateTestAreasAndSuiteOrders()
    {
        using var root = TemporaryConfiguration.Create();
        var suitesPath = root.ConfigPath("test-suites.json");
        var suites = JsonNode.Parse(File.ReadAllText(suitesPath))!;
        suites["areas"]!.AsArray().Add("Search");
        File.WriteAllText(suitesPath, suites.ToJsonString());

        var areaError = Assert.Throws<InvalidDataException>(() => root.Load());
        Assert.Contains("duplicate test area 'Search'", areaError.Message, StringComparison.Ordinal);

        root.Restore();
        suites = JsonNode.Parse(File.ReadAllText(suitesPath))!;
        suites["data"]!["core"]!["Order"] = 10;
        suites["data"]!["text"]!["Order"] = 10;
        File.WriteAllText(suitesPath, suites.ToJsonString());

        var orderError = Assert.Throws<InvalidDataException>(() => root.Load());
        Assert.Contains("suite order '10' is already assigned", orderError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsQualificationEligibilityFromSuiteMetadata()
    {
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        Assert.True(configuration.TestSuites["dataforge"].DataForgeQualification);
        Assert.True(configuration.TestSuites["architecture"].DataForgeQualification);
        Assert.True(configuration.TestSuites["server-integration"].DataForgeQualification);
        Assert.False(configuration.TestSuites["docs-site"].DataForgeQualification);
    }

    [Fact]
    public void LoadsVersionedRequiredDataForgeProfileIdsAndVersions()
    {
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        Assert.Equal(new[]
        {
            new DataForgeProfileRequirement("leancorpus-hybrid", 1),
            new DataForgeProfileRequirement("leancorpus-search", 1),
            new DataForgeProfileRequirement("leancorpus-vector", 1),
            new DataForgeProfileRequirement("rowles-text-multilingual", 1)
        }, configuration.RequiredDataForgeSyntheticProfiles);
    }

    [Fact]
    public void RejectsMalformedDataForgeProfilePolicyAndNonPositiveVersions()
    {
        using var root = TemporaryConfiguration.Create();
        var path = root.ConfigPath("dataforge-qualification.json");
        var document = JsonNode.Parse(File.ReadAllText(path))!;
        document["schemaVersion"] = 99;
        File.WriteAllText(path, document.ToJsonString());

        var versionError = Assert.Throws<InvalidDataException>(() => root.Load());
        Assert.Contains("unsupported version 99", versionError.Message, StringComparison.Ordinal);

        root.Restore();
        document = JsonNode.Parse(File.ReadAllText(path))!;
        document["data"]!["requiredSyntheticProfiles"]!["leancorpus-search"] = 0;
        File.WriteAllText(path, document.ToJsonString());

        var profileError = Assert.Throws<InvalidDataException>(() => root.Load());
        Assert.Contains("profile version must be positive", profileError.Message, StringComparison.Ordinal);
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
            CopyAuditReferences(checkout.RepositoryRoot, path);

            return new TemporaryConfiguration(path, sourceRoot);
        }

        private static void CopyAuditReferences(string sourceRepositoryRoot, string destinationRepositoryRoot)
        {
            var auditPath = System.IO.Path.Combine(sourceRepositoryRoot, ".config", "devops", "benchmark-correctness-audit.json");
            using var audit = JsonDocument.Parse(File.ReadAllBytes(auditPath));
            foreach (var gate in audit.RootElement.GetProperty("gates").EnumerateArray())
            {
                foreach (var propertyName in new[] { "sourceFile", "fixturePath" })
                {
                    var relativePath = gate.GetProperty(propertyName).GetString()!;
                    var sourcePath = System.IO.Path.Combine(sourceRepositoryRoot, relativePath);
                    var destinationPath = System.IO.Path.Combine(destinationRepositoryRoot, relativePath);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationPath)!);
                    File.Copy(sourcePath, destinationPath, overwrite: true);
                }
            }
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

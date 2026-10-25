using System.Text.Json;
using Rowles.LeanCorpus.Cli;
using Rowles.LeanCorpus.Cli.Creation;
using Rowles.LeanCorpus.Cli.Rendering;
using Rowles.LeanCorpus.Cli.TestLab;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Inspection;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Spectre.Console.Testing;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class WorkbenchCommandsTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public WorkbenchCommandsTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Workbench: inspection does not create missing directories")]
    public void Inspector_MissingDirectory_IsNotCreated()
    {
        string path = Path.Combine(_fixture.Path, "missing-inspection-directory");
        Assert.Throws<ArgumentException>(() => new IndexInspector(path));
        Assert.False(Directory.Exists(path));
    }

    [Fact(DisplayName = "Workbench: physical inspection reports file count and bytes")]
    public void Inspector_PhysicalStatistics_MatchDirectoryFiles()
    {
        string path = CreateSimpleIndex("inspection-statistics");
        using var inspector = new IndexInspector(path);
        var files = Directory.EnumerateFiles(path).Select(static file => new FileInfo(file)).ToArray();
        Assert.Equal(files.Length, inspector.Overview.FileCount);
        Assert.Equal(files.Sum(static file => file.Length), inspector.Overview.TotalSizeBytes);
    }

    [Theory(DisplayName = "Workbench: invalid browser input returns to the menu")]
    [InlineData(4, "-1")]
    [InlineData(5, "(")]
    public void Browser_InvalidInputCanReturnToMenuAndExit(int viewIndex, string input)
    {
        string source = CreateSimpleIndex($"workbench-browser-invalid-{viewIndex}");
        var console = new TestConsole().Interactive();
        for (int i = 0; i < viewIndex; i++)
            console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        if (viewIndex == 5)
            console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter(input);
        for (int i = 0; i < 12; i++)
            console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        using var inspector = new IndexInspector(source);

        Assert.Equal(CliExitCodes.Success, WorkbenchBrowser.Browse(console, inspector));
        Assert.Contains("Unable to open this view", console.Output);
        Assert.Contains("LeanCorpus CLI Workbench", console.Output);
    }

    [Fact(DisplayName = "Workbench: compaction releases its writer before verifying the result")]
    public void Compact_MergesCopiedIndexAndPreservesSource()
    {
        string source = CreateSimpleIndex("workbench-compact-source");
        string mutated = Path.Combine(_fixture.Path, "workbench-compact-mutated");
        string destination = Path.Combine(_fixture.Path, "workbench-compact-result");
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(CliExitCodes.Success, IndexCheckerCli.Run(
            ["testlab", "mutate", source, mutated, "--mode", "many-small-segments", "--count", "4"], output, error));
        IndexFingerprintResult before = IndexFingerprinter.Calculate(mutated);
        Assert.True(before.SegmentCount > 1);

        int exitCode = IndexCheckerCli.Run(["transform", "compact", mutated, destination, "--segments", "1"], output, error);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Empty(error.ToString());
        IndexFingerprintResult after = IndexFingerprinter.Calculate(destination);
        Assert.Equal(1, after.SegmentCount);
        Assert.Equal(before.LogicalFingerprint, after.LogicalFingerprint);
        Assert.Equal(before.PhysicalFingerprint, IndexFingerprinter.Calculate(mutated).PhysicalFingerprint);
    }

    [Fact(DisplayName = "Workbench: redirected analysis is stable plain text")]
    public void Analyse_RedirectedOutputUsesPlainFallback()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["analyse", "Hello, world!"], output, error);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("hello", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\u001b', output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact(DisplayName = "Workbench: DataForge preset writes a reproducible recipe and manifest")]
    public void CreatePreset_WritesRecipeAndDataForgeManifest()
    {
        string path = Path.Combine(_fixture.Path, "workbench-created-preset");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(
            ["create", "preset", "search-small", path, "--seed", "42", "--count", "8", "--json"], output, error);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Empty(error.ToString());
        using JsonDocument report = JsonDocument.Parse(output.ToString());
        Assert.Equal(8, report.RootElement.GetProperty("DocumentCount").GetInt32());
        string recipePath = Path.Combine(path, ".leancorpus", "recipe.json");
        string dataForgeManifestPath = Path.Combine(path, ".leancorpus", "dataforge-manifest.json");
        Assert.True(File.Exists(recipePath));
        Assert.True(File.Exists(dataForgeManifestPath));
        using JsonDocument recipe = JsonDocument.Parse(File.ReadAllBytes(recipePath));
        Assert.Equal("search-small", recipe.RootElement.GetProperty("preset").GetString());
        Assert.False(string.IsNullOrWhiteSpace(recipe.RootElement.GetProperty("recipeIdentity").GetString()));
    }

    [Fact(DisplayName = "Workbench: recipe reproduction compares logically equal")]
    public void Reproduce_ProducesLogicallyEquivalentIndex()
    {
        string source = Path.Combine(_fixture.Path, "workbench-reproduce-source");
        string destination = Path.Combine(_fixture.Path, "workbench-reproduce-copy");
        using var createOutput = new StringWriter();
        using var createError = new StringWriter();
        using var reproduceOutput = new StringWriter();
        using var reproduceError = new StringWriter();
        using var diffOutput = new StringWriter();
        using var diffError = new StringWriter();

        int createExit = IndexCheckerCli.Run(
            ["create", "preset", "search-small", source, "--seed", "987", "--count", "8"], createOutput, createError);
        int reproduceExit = IndexCheckerCli.Run(["create", "reproduce", source, destination], reproduceOutput, reproduceError);
        int diffExit = IndexCheckerCli.Run(["index", "diff", source, destination, "--logical", "--json"], diffOutput, diffError);

        Assert.Equal(CliExitCodes.Success, createExit);
        Assert.Equal(CliExitCodes.Success, reproduceExit);
        Assert.Equal(CliExitCodes.Success, diffExit);
        Assert.Empty(createError.ToString());
        Assert.Empty(reproduceError.ToString());
        Assert.Empty(diffError.ToString());
        using JsonDocument diff = JsonDocument.Parse(diffOutput.ToString());
        Assert.True(diff.RootElement.GetProperty("LogicalEquivalent").GetBoolean());
    }

    [Fact(DisplayName = "Workbench: corruption writes deterministic manifest without changing source")]
    public void Corrupt_CopiesAndRecordsMutation()
    {
        string source = CreateSimpleIndex("workbench-corrupt-source");
        string destination = Path.Combine(_fixture.Path, "workbench-corrupt-copy");
        string sourceFingerprint = IndexFingerprinter.Calculate(source).PhysicalFingerprint;
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(
            ["testlab", "corrupt", source, destination, "--component", "postings", "--mode", "truncate", "--seed", "7"], output, error);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Empty(error.ToString());
        Assert.Equal(sourceFingerprint, IndexFingerprinter.Calculate(source).PhysicalFingerprint);
        using JsonDocument mutation = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(destination, ".leancorpus", "mutation.json")));
        Assert.Equal("postings", mutation.RootElement.GetProperty("Component").GetString());
        Assert.True(mutation.RootElement.TryGetProperty("BaseLogicalFingerprint", out _));
        Assert.True(mutation.RootElement.TryGetProperty("BaseRecipeIdentity", out _));
        Assert.Equal("truncate", mutation.RootElement.GetProperty("Mode").GetString());
        Assert.Equal(7UL, mutation.RootElement.GetProperty("Seed").GetUInt64());
        Assert.False(string.IsNullOrWhiteSpace(mutation.RootElement.GetProperty("TargetFile").GetString()));
    }

    [Fact(DisplayName = "Workbench: frozen migration preset preserves release fixture provenance")]
    public void CodecMigrationPreset_InstallsPinnedReleaseFixture()
    {
        string path = Path.Combine(_fixture.Path, "workbench-codec-migration-required");
        IndexCreationResult result = IndexCreator.CreatePreset("codec-migration-required", path, 42, null, 32, "Clustered", "en");

        Assert.Equal(8, result.DocumentCount);
        Assert.True(File.Exists(Path.Combine(path, ".leancorpus", "migration-fixture-manifest.json")));
        using JsonDocument recipe = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(path, ".leancorpus", "recipe.json")));
        Assert.Equal("fixture", recipe.RootElement.GetProperty("mode").GetString());
        Assert.Equal("codec-migration-required", recipe.RootElement.GetProperty("preset").GetString());
        Assert.Equal(result.RecipeIdentity, recipe.RootElement.GetProperty("recipeIdentity").GetString());
        using var inspector = new IndexInspector(path);
        Assert.Equal(8, inspector.Overview.DocumentCount);
        Assert.True(inspector.Overview.HasVectors);
    }

    [Fact(DisplayName = "Workbench: Spectre renderer writes structured field table")]
    public void SpectreFieldsRenderer_RendersFieldCapabilities()
    {
        var console = new TestConsole();
        IndexInspectionRenderer.Fields(console,
        [
            new IndexInspectionField("body", true, true, false, true, false, 1, 2, 3, 4, null, null, null, null, null, null)
        ]);

        Assert.Contains("body", console.Output);
        Assert.Contains("Indexed", console.Output);
        Assert.Contains("Stored", console.Output);
    }

    [Fact(DisplayName = "Workbench: workload replay checks deterministic expected results")]
    public void WorkloadCreationAndReplay_PreservesExpectedResults()
    {
        string path = Path.Combine(_fixture.Path, "workbench-workload");
        using var createOutput = new StringWriter();
        using var createError = new StringWriter();
        using var replayOutput = new StringWriter();
        using var replayError = new StringWriter();

        int createExit = IndexCheckerCli.Run(
            ["create", "workload", path, "--seed", "42", "--count", "8", "--queries", "8", "--json"], createOutput, createError);
        int replayExit = IndexCheckerCli.Run(["search", "replay", path, "--json"], replayOutput, replayError);

        Assert.Equal(CliExitCodes.Success, createExit);
        Assert.Equal(CliExitCodes.Success, replayExit);
        Assert.Empty(createError.ToString());
        Assert.Empty(replayError.ToString());
        using JsonDocument replay = JsonDocument.Parse(replayOutput.ToString());
        Assert.Equal(8, replay.RootElement.GetProperty("QueryCount").GetInt32());
        Assert.Equal(0, replay.RootElement.GetProperty("FailureCount").GetInt32());
        Assert.Equal(0, replay.RootElement.GetProperty("ResultDifferenceCount").GetInt32());
        Assert.True(File.Exists(Path.Combine(path, "dataset", "manifest.json")));
        Assert.True(File.Exists(Path.Combine(path, "recipe.json")));
    }

    [Fact(DisplayName = "Workbench: custom schema recipe reproduces logical content")]
    public void CustomSchemaCreationAndReproduction_PreservesLogicalFingerprint()
    {
        string schema = Path.Combine(_fixture.Path, "workbench-custom-schema.json");
        string source = Path.Combine(_fixture.Path, "workbench-custom-schema-index");
        string recreated = Path.Combine(_fixture.Path, "workbench-custom-schema-recreated");
        File.WriteAllText(schema, """
            {
              "schemaVersion": 1,
              "compound": true,
              "fields": [
                { "name": "title", "type": "text", "source": "title", "stored": true, "positions": true },
                { "name": "category", "type": "keyword", "source": "category", "stored": true, "docValues": true, "facet": true },
                { "name": "price", "type": "double", "source": "price", "stored": true, "docValues": true },
                { "name": "embedding", "type": "vector", "dimensions": 8 },
                { "name": "location", "type": "geoPoint" }
              ]
            }
            """);
        using var createOutput = new StringWriter();
        using var createError = new StringWriter();
        using var reproduceOutput = new StringWriter();
        using var reproduceError = new StringWriter();
        using var diffOutput = new StringWriter();
        using var diffError = new StringWriter();

        int createExit = IndexCheckerCli.Run(["create", "custom", source, "--schema", schema, "--seed", "321", "--count", "8"], createOutput, createError);
        int reproduceExit = IndexCheckerCli.Run(["create", "reproduce", source, recreated], reproduceOutput, reproduceError);
        int diffExit = IndexCheckerCli.Run(["index", "diff", source, recreated, "--logical", "--json"], diffOutput, diffError);

        Assert.Equal(CliExitCodes.Success, createExit);
        Assert.Equal(CliExitCodes.Success, reproduceExit);
        Assert.Equal(CliExitCodes.Success, diffExit);
        Assert.Empty(createError.ToString());
        Assert.Empty(reproduceError.ToString());
        Assert.Empty(diffError.ToString());
        using JsonDocument diff = JsonDocument.Parse(diffOutput.ToString());
        Assert.True(diff.RootElement.GetProperty("LogicalEquivalent").GetBoolean());
        Assert.True(File.Exists(Path.Combine(source, ".leancorpus", "schema.json")));
        Assert.True(File.Exists(Path.Combine(source, ".leancorpus", "dataforge-manifest.json")));
    }

    [Fact(DisplayName = "Workbench: mutation and fixture packaging preserve the source index")]
    public void MutateAndFixtureRoundTrip_KeepSourceFingerprint()
    {
        string source = CreateSimpleIndex("workbench-mutate-source");
        string destination = Path.Combine(_fixture.Path, "workbench-mutated-copy");
        string package = Path.Combine(_fixture.Path, "workbench-fixture.lcf");
        string unpacked = Path.Combine(_fixture.Path, "workbench-fixture-unpacked");
        string fingerprint = IndexFingerprinter.Calculate(source).PhysicalFingerprint;
        using var mutateOutput = new StringWriter();
        using var mutateError = new StringWriter();
        using var packOutput = new StringWriter();
        using var packError = new StringWriter();
        using var unpackOutput = new StringWriter();
        using var unpackError = new StringWriter();

        int mutateExit = IndexCheckerCli.Run(["testlab", "mutate", source, destination, "--mode", "add", "--seed", "9", "--count", "2"], mutateOutput, mutateError);
        int packExit = IndexCheckerCli.Run(["testlab", "fixture", "pack", destination, package], packOutput, packError);
        int unpackExit = IndexCheckerCli.Run(["testlab", "fixture", "unpack", package, unpacked], unpackOutput, unpackError);

        Assert.Equal(CliExitCodes.Success, mutateExit);
        Assert.Equal(CliExitCodes.Success, packExit);
        Assert.Equal(CliExitCodes.Success, unpackExit);
        Assert.Empty(mutateError.ToString());
        Assert.Empty(packError.ToString());
        Assert.Empty(unpackError.ToString());
        Assert.Equal(fingerprint, IndexFingerprinter.Calculate(source).PhysicalFingerprint);
        Assert.True(File.Exists(Path.Combine(destination, ".leancorpus", "mutation.json")));
        Assert.True(File.Exists(Path.Combine(unpacked, "segments_1")) || Directory.EnumerateFiles(unpacked).Any());
    }

    [Fact(DisplayName = "Workbench: logical fingerprint includes per-document indexed terms")]
    public void Fingerprint_DistinguishesTermAssignmentsWithoutStoredFields()
    {
        string first = CreateTermOnlyIndex("workbench-term-assignment-first", ["apple banana", "apple cherry", "banana cherry", ""]);
        string second = CreateTermOnlyIndex("workbench-term-assignment-second", ["apple banana cherry", "apple", "banana", "cherry"]);

        IndexFingerprintResult firstFingerprint = IndexFingerprinter.Calculate(first);
        IndexFingerprintResult secondFingerprint = IndexFingerprinter.Calculate(second);

        Assert.NotEqual(firstFingerprint.LogicalFingerprint, secondFingerprint.LogicalFingerprint);
    }

    [Fact(DisplayName = "Workbench: seed search evaluates a bounded seed range")]
    public void SeedSearch_ReportsRangeAndRemovesItsTemporaryFixtures()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(
            ["testlab", "seed-search", "--from", "5", "--to", "5", "--find", "zero-results", "--count", "8", "--json"], output, error);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Empty(error.ToString());
        using JsonDocument report = JsonDocument.Parse(output.ToString());
        Assert.Equal(5UL, report.RootElement.GetProperty("FirstSeed").GetUInt64());
        Assert.Equal(5UL, report.RootElement.GetProperty("LastSeed").GetUInt64());
        Assert.Equal(1, report.RootElement.GetProperty("SeedsEvaluated").GetInt32());
    }

    private string CreateSimpleIndex(string name)
    {
        string path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        var document = new LeanDocument();
        document.Add(new StringField("id", "doc-1"));
        document.Add(new TextField("body", "workbench corruption sample", stored: true));
        writer.AddDocument(document);
        writer.Commit();
        return path;
    }

    private string CreateTermOnlyIndex(string name, IReadOnlyList<string> documents)
    {
        string path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        foreach (string content in documents)
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", content, stored: false));
            writer.AddDocument(document);
        }
        writer.Commit();
        return path;
    }
}

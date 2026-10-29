using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class BenchmarkCaseCoverageTests
{
    [Fact]
    public void UsesBenchmarkDotNetDeclaringTypeAndMethodForQuotedDottedDescriptions()
    {
        using var directory = TemporaryDirectory.Create();
        var invocationDirectory = Path.Combine(directory.Path, "core", "000");
        var resultsDirectory = Path.Combine(invocationDirectory, "_runner", "results");
        Directory.CreateDirectory(resultsDirectory);
        const string displayInfo = "VectorQuantisationBenchmarks.'Lucene.NET flat scan': Dry(Arguments=-m:1) [Dimension=64, Quantisation=BBQ]";
        File.WriteAllLines(Path.Combine(invocationDirectory, "process.stdout.log"),
        [
            "// Found 1 benchmarks:",
            "//   " + displayInfo,
            "// **************************",
            "// Benchmark: " + displayInfo,
            "// Benchmark Process 100 has exited with code 255."
        ]);
        File.WriteAllText(Path.Combine(resultsDirectory, "VectorQuantisationBenchmarks-report-full.json"),
            JsonSerializer.Serialize(new
            {
                Benchmarks = new[]
                {
                    new
                    {
                        DisplayInfo = displayInfo,
                        Namespace = "Rowles.LeanCorpus.Benchmarks",
                        Type = "VectorQuantisationBenchmarks",
                        Method = "LuceneNet_Baseline",
                        MethodTitle = "'Lucene.NET flat scan'",
                        FullName = "Rowles.LeanCorpus.Benchmarks.VectorQuantisationBenchmarks.LuceneNet_Baseline(Dimension: 64, Quantisation: BBQ)",
                        Statistics = (object?)null,
                        Result = (object?)null
                    }
                }
            }));

        var plan = new BenchmarkExecutionPlan("core", "fast", "net10.0", "synthetic", 500, false, null,
            [new BenchmarkInvocation("core", "benchmarks.csproj", [], null)], [], []);
        var result = new BenchmarkProjectResult("core", "Failed", 1, "core/000", "recall floor");
        var coverage = BenchmarkCaseCoverage.Create(directory.Path, plan, [result], OutcomeKind.CommandFailure, null);

        var benchmarkCase = Assert.Single(coverage.Cases);
        Assert.Equal("Rowles.LeanCorpus.Benchmarks.VectorQuantisationBenchmarks", benchmarkCase.BenchmarkClass);
        Assert.Equal("LuceneNet_Baseline", typeof(BenchmarkCaseCoverageRecord)
            .GetProperty("BenchmarkMethod")?.GetValue(benchmarkCase));
        Assert.Equal("*Rowles.LeanCorpus.Benchmarks.VectorQuantisationBenchmarks*",
            Assert.Single(coverage.TargetedRerunArguments)[^1]);
    }

    [Fact]
    public void KeepsDuplicateDescriptionsSeparateByQualifiedBenchmarkIdentity()
    {
        using var directory = TemporaryDirectory.Create();
        var invocationDirectory = Path.Combine(directory.Path, "core", "000");
        var resultsDirectory = Path.Combine(invocationDirectory, "_runner", "results");
        Directory.CreateDirectory(resultsDirectory);
        const string displayInfo = "SharedBenchmarks.'Lucene.NET: quoted scan': Dry(Arguments=-m:1) [Count=1]";
        File.WriteAllLines(Path.Combine(invocationDirectory, "process.stdout.log"),
        [
            "// Found 2 benchmarks:",
            "//   " + displayInfo,
            "//   " + displayInfo,
            "// **************************",
            "// Benchmark: " + displayInfo,
            "// Execute: dotnet Runner.dll --benchmarkName \"First.Namespace.SharedBenchmarks.RunFirst(Count: 1)\" --job Dry",
            "// Benchmark Process 100 has exited with code 255.",
            "// Benchmark: " + displayInfo,
            "// Execute: dotnet Runner.dll --benchmarkName \"Second.Namespace.SharedBenchmarks.RunSecond(Count: 1)\" --job Dry",
            "// Benchmark Process 101 has exited with code 255."
        ]);
        File.WriteAllText(Path.Combine(resultsDirectory, "SharedBenchmarks-report-full.json"),
            JsonSerializer.Serialize(new
            {
                Benchmarks = new[]
                {
                    new
                    {
                        DisplayInfo = displayInfo,
                        Namespace = "First.Namespace",
                        Type = "SharedBenchmarks",
                        Method = "RunFirst",
                        FullName = "First.Namespace.SharedBenchmarks.RunFirst(Count: 1)",
                        Statistics = (object?)null,
                        Result = (object?)null
                    },
                    new
                    {
                        DisplayInfo = displayInfo,
                        Namespace = "Second.Namespace",
                        Type = "SharedBenchmarks",
                        Method = "RunSecond",
                        FullName = "Second.Namespace.SharedBenchmarks.RunSecond(Count: 1)",
                        Statistics = (object?)null,
                        Result = (object?)null
                    }
                }
            }));

        var result = new BenchmarkProjectResult("core", "Failed", 1, "core/000", "child failed");
        var coverage = BenchmarkCaseCoverage.Create(directory.Path, CreateUnfilteredPlan(), [result], OutcomeKind.CommandFailure, null);

        Assert.Equal(2, coverage.PlannedCaseCount);
        Assert.Equal(2, coverage.FailedCaseCount);
        Assert.Collection(coverage.Cases.OrderBy(item => item.BenchmarkClass, StringComparer.Ordinal),
            first =>
            {
                Assert.Equal("First.Namespace.SharedBenchmarks", first.BenchmarkClass);
                Assert.Equal("RunFirst", typeof(BenchmarkCaseCoverageRecord).GetProperty("BenchmarkMethod")?.GetValue(first));
            },
            second =>
            {
                Assert.Equal("Second.Namespace.SharedBenchmarks", second.BenchmarkClass);
                Assert.Equal("RunSecond", typeof(BenchmarkCaseCoverageRecord).GetProperty("BenchmarkMethod")?.GetValue(second));
            });
        Assert.Equal(
            ["*First.Namespace.SharedBenchmarks*", "*Second.Namespace.SharedBenchmarks*"],
            coverage.TargetedRerunArguments.Select(arguments => arguments[^1]).ToArray());
    }

    [Fact]
    public void DoesNotCountAFormerBenchmarkSummaryMeanAsTheLastFailedCase()
    {
        using var directory = TemporaryDirectory.Create();
        var invocationDirectory = Path.Combine(directory.Path, "core", "000");
        Directory.CreateDirectory(invocationDirectory);
        File.WriteAllLines(Path.Combine(invocationDirectory, "process.stdout.log"),
        [
            "// Found 2 benchmarks:",
            "//   ExampleBenchmarks.Passed: Job-1 [Count=1]",
            "//   ExampleBenchmarks.Failed: Job-1 [Count=2]",
            "// **************************",
            "// Benchmark: ExampleBenchmarks.Passed: Job-1 [Count=1]",
            "// Benchmark Process 100 has exited with code 0.",
            "Mean = 12.0 ns, StdErr = 0.1 ns, N = 5",
            "// Benchmark: ExampleBenchmarks.Failed: Job-1 [Count=2]",
            "// Benchmark Process 101 has exited with code 255.",
            "// * Detailed results *",
            "ExampleBenchmarks.Passed: Job-1 [Count=1]",
            "Mean = 12.0 ns, StdErr = 0.1 ns, N = 5",
            "ExampleBenchmarks.Failed: Job-1 [Count=2]",
            "There are not any results runs"
        ]);

        var result = new BenchmarkProjectResult("core", "Failed", 1, "core/000", "child failed");
        var coverage = BenchmarkCaseCoverage.Create(directory.Path, CreateUnfilteredPlan(), [result], OutcomeKind.CommandFailure, null);

        Assert.Equal(1, coverage.CompletedCaseCount);
        Assert.Equal(1, coverage.FailedCaseCount);
        Assert.Equal("failed", Assert.Single(coverage.Cases, item => item.DisplayInfo.Contains("Failed:", StringComparison.Ordinal)).State);
    }

    [Fact]
    public void AggregatesDiscoveryInventoriesAcrossMultipleBenchmarkDotNetGroups()
    {
        using var directory = TemporaryDirectory.Create();
        var invocationDirectory = Path.Combine(directory.Path, "text", "000");
        Directory.CreateDirectory(invocationDirectory);
        File.WriteAllLines(Path.Combine(invocationDirectory, "process.stdout.log"),
        [
            "// Found 2 benchmarks:",
            "//   FirstBenchmarks.One: Job-1 [Count=1]",
            "//   FirstBenchmarks.Two: Job-1 [Count=2]",
            "// **************************",
            "// Benchmark: FirstBenchmarks.One: Job-1 [Count=1]",
            "Mean = 12.0 ns, StdErr = 0.1 ns, N = 5",
            "// Benchmark: FirstBenchmarks.Two: Job-1 [Count=2]",
            "Mean = 14.0 ns, StdErr = 0.1 ns, N = 5",
            "// Found 1 benchmarks:",
            "//   SecondBenchmarks.Three: Job-1 [Count=3]",
            "// **************************",
            "// Benchmark: SecondBenchmarks.Three: Job-1 [Count=3]",
            "Mean = 16.0 ns, StdErr = 0.1 ns, N = 5"
        ]);

        var result = new BenchmarkProjectResult("text", "Passed", 0, "text/000", null);
        var coverage = BenchmarkCaseCoverage.Create(directory.Path,
            new BenchmarkExecutionPlan("text", "fast", "net10.0", "synthetic", 500, false, null,
                [new BenchmarkInvocation("text", "src/devops/benchmarks/Rowles.Text.Benchmarks/Rowles.Text.Benchmarks.csproj", [], null)], [], []),
            [result], OutcomeKind.Success, null);

        Assert.True(coverage.CompleteInventoryKnown);
        Assert.Equal(3, coverage.PlannedCaseCount);
        Assert.Equal(3, coverage.CompletedCaseCount);
        Assert.Equal(3, Assert.Single(coverage.Invocations).PlannedCaseCount);
        Assert.Empty(Assert.Single(coverage.Invocations).Diagnostics);
    }

    [Fact]
    public void AccountsForCompletedFailedAndNeverStartedCasesAndBuildsClassRerunPlan()
    {
        using var directory = TemporaryDirectory.Create();
        var invocationDirectory = Path.Combine(directory.Path, "core", "000");
        Directory.CreateDirectory(invocationDirectory);
        var stdoutPath = Path.Combine(invocationDirectory, "process.stdout.log");
        File.WriteAllLines(stdoutPath,
        [
            "// Found 4 benchmarks:",
            "//   ExampleBenchmarks.Completed: Job-1 [Count=1]",
            "//   ExampleBenchmarks.CompletedFromFile: Job-1 [Count=2]",
            "//   ExampleBenchmarks.Failed: Job-1 [Count=3]",
            "//   ExampleBenchmarks.NeverStarted: Job-1 [Count=4]",
            "// **************************",
            "// Benchmark: ExampleBenchmarks.Completed: Job-1 [Count=1]",
            "// Benchmark Process 100 has exited with code 0.",
            "Mean = 12.0 ns, StdErr = 0.1 ns, N = 5",
            "// Benchmark: ExampleBenchmarks.CompletedFromFile: Job-1 [Count=2]",
            "// Benchmark: ExampleBenchmarks.Failed: Job-1 [Count=3]",
            "// Benchmark Process 102 has exited with code 1."
        ]);
        var resultsDirectory = Path.Combine(invocationDirectory, "_runner", "results");
        Directory.CreateDirectory(resultsDirectory);
        var reportPath = Path.Combine(resultsDirectory, "ExampleBenchmarks-report-full.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            Benchmarks = new[]
            {
                new
                {
                    DisplayInfo = "ExampleBenchmarks.CompletedFromFile: Job-1 [Count=2]",
                    Statistics = new { N = 5 },
                    Result = (object?)null
                }
            }
        }));

        var plan = CreatePlan();
        var result = new BenchmarkProjectResult("core/ExampleBenchmarks", "Failed", 1, "core/000", "child failed");
        var coverage = BenchmarkCaseCoverage.Create(directory.Path, plan, [result], OutcomeKind.CommandFailure, null);

        Assert.True(coverage.CompleteInventoryKnown);
        Assert.Equal(4, coverage.PlannedCaseCount);
        Assert.Equal(3, coverage.StartedCaseCount);
        Assert.Equal(2, coverage.CompletedCaseCount);
        Assert.Equal(0, coverage.InterruptedCaseCount);
        Assert.Equal(1, coverage.FailedCaseCount);
        Assert.Equal(1, coverage.NeverStartedCaseCount);
        Assert.Equal("completed", Assert.Single(coverage.Cases, item => item.DisplayInfo.Contains("CompletedFromFile", StringComparison.Ordinal)).State);
        Assert.Contains(coverage.Cases.Single(item => item.DisplayInfo.Contains("CompletedFromFile", StringComparison.Ordinal)).ResultLocations,
            location => location.EndsWith("ExampleBenchmarks-report-full.json", StringComparison.Ordinal));
        Assert.Equal("failed", coverage.Cases.Single(item => item.DisplayInfo.Contains("Failed:", StringComparison.Ordinal)).State);
        Assert.Equal("never-started", coverage.Cases.Single(item => item.DisplayInfo.Contains("NeverStarted:", StringComparison.Ordinal)).State);
        Assert.Contains(coverage.Cases.Single(item => item.DisplayInfo.Contains("NeverStarted:", StringComparison.Ordinal)).ResultLocations,
            location => location.EndsWith("process.stdout.log#L5", StringComparison.Ordinal));
        Assert.Equal("ExampleBenchmarks", Assert.Single(coverage.TargetedRerunArguments)[^1][1..^1]);
        Assert.False(coverage.ExistingResultsMayBeStitched);
    }

    [Fact]
    public void SeparatesInterruptedCurrentCaseFromNeverStartedCases()
    {
        using var directory = TemporaryDirectory.Create();
        var invocationDirectory = Path.Combine(directory.Path, "core", "000");
        Directory.CreateDirectory(invocationDirectory);
        File.WriteAllLines(Path.Combine(invocationDirectory, "process.stdout.log"),
        [
            "// Found 3 benchmarks:",
            "//   ExampleBenchmarks.Completed: Job-1 [Count=1]",
            "//   ExampleBenchmarks.Interrupted: Job-1 [Count=2]",
            "//   ExampleBenchmarks.NeverStarted: Job-1 [Count=3]",
            "// **************************",
            "// Benchmark: ExampleBenchmarks.Completed: Job-1 [Count=1]",
            "// Benchmark Process 100 has exited with code 0.",
            "Mean = 12.0 ns, StdErr = 0.1 ns, N = 5",
            "// Benchmark: ExampleBenchmarks.Interrupted: Job-1 [Count=2]",
            "WorkloadActual   1: 1 op, 100 ns, 100 ns/op"
        ]);

        var plan = CreatePlan();
        var result = new BenchmarkProjectResult("core/ExampleBenchmarks", "Interrupted", null, "core/000", "aggregate timeout",
            RunDuration: TimeSpan.FromSeconds(2), TimeoutSource: "aggregate");
        var coverage = BenchmarkCaseCoverage.Create(directory.Path, plan, [result], OutcomeKind.Timeout, "aggregate");

        Assert.Equal(1, coverage.CompletedCaseCount);
        Assert.Equal(1, coverage.InterruptedCaseCount);
        Assert.Equal(1, coverage.NeverStartedCaseCount);
        Assert.Equal("aggregate", coverage.TimeoutSource);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(coverage.Invocations).RunDuration);
        Assert.Contains("ExampleBenchmarks", Assert.Single(coverage.TargetedRerunArguments).Last(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsUnknownCaseInventoryInsteadOfTreatingAnUnstartedChildAsZeroCases()
    {
        using var directory = TemporaryDirectory.Create();
        var plan = CreatePlan();
        var result = new BenchmarkProjectResult("core/ExampleBenchmarks", "Interrupted", null, "core/000",
            "aggregate timeout", TimeoutSource: "aggregate");

        var coverage = BenchmarkCaseCoverage.Create(directory.Path, plan, [result], OutcomeKind.Timeout, "aggregate");

        Assert.Equal(0, coverage.PlannedCaseCount);
        Assert.False(coverage.CompleteInventoryKnown);
        Assert.Equal(1, coverage.UnenumeratedInvocationCount);
        Assert.Equal(1, coverage.StartedInvocationCount);
        Assert.Equal("interrupted", Assert.Single(coverage.Invocations).State);
        Assert.Single(coverage.TargetedRerunArguments);
        Assert.Equal("core", coverage.TargetedRerunArguments[0][2]);
    }

    [Fact]
    public void ReportsExplicitWorkloadsAsExcludedFromAllAndIncludedByAllWithExplicit()
    {
        using var directory = TemporaryDirectory.Create();
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var allPlan = CreateSelectorPlan("all");
        var allCoverage = BenchmarkCaseCoverage.Create(directory.Path, allPlan, [], OutcomeKind.Success, null);
        var all = JsonSerializer.SerializeToElement(allCoverage, jsonOptions);

        Assert.Equal("all", all.GetProperty("selectionScope").GetString());
        var stress = Assert.Single(all.GetProperty("excludedWorkloads").EnumerateArray(), workload =>
            workload.GetProperty("suiteId").GetString() == "async-index-stress");
        Assert.Equal("excluded", stress.GetProperty("state").GetString());
        Assert.Equal("explicit-only", stress.GetProperty("reason").GetString());
        Assert.Equal("AsyncIndexingCapacityOneStressBenchmarks",
            Assert.Single(stress.GetProperty("benchmarkClasses").EnumerateArray()).GetString());

        var allWithExplicitPlan = CreateSelectorPlan("all-with-explicit");
        var allWithExplicitCoverage = BenchmarkCaseCoverage.Create(directory.Path, allWithExplicitPlan, [], OutcomeKind.Success, null);
        var allWithExplicit = JsonSerializer.SerializeToElement(allWithExplicitCoverage, jsonOptions);

        Assert.Equal("all-with-explicit", allWithExplicit.GetProperty("selectionScope").GetString());
        Assert.DoesNotContain(allWithExplicit.GetProperty("excludedWorkloads").EnumerateArray(), workload =>
            workload.GetProperty("suiteId").GetString() == "async-index-stress");
    }

    [Fact]
    public void SummarisesSelectedProjectCasesAndElapsedTimeWithoutInventingUnknownCounts()
    {
        using var directory = TemporaryDirectory.Create();
        var plan = CreateSelectorPlan("all");
        var compression = Path.Combine(directory.Path, "compression", "000");
        Directory.CreateDirectory(compression);
        File.WriteAllLines(Path.Combine(compression, "process.stdout.log"),
        [
            "// Found 2 benchmarks:",
            "//   ExampleBenchmarks.Completed: Job-1 [Count=1]",
            "//   ExampleBenchmarks.NotStarted: Job-1 [Count=2]",
            "// **************************",
            "// Benchmark: ExampleBenchmarks.Completed: Job-1 [Count=1]",
            "Mean = 12.0 ns, StdErr = 0.1 ns, N = 5"
        ]);
        var result = new BenchmarkProjectResult("compression", "Passed", 0, "compression/000", null,
            BuildDuration: TimeSpan.FromSeconds(2), RunDuration: TimeSpan.FromSeconds(5));

        var coverage = BenchmarkCaseCoverage.Create(directory.Path, plan, [result], OutcomeKind.Success, null);
        var json = JsonSerializer.SerializeToElement(coverage,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var projects = json.GetProperty("projects").EnumerateArray().ToArray();

        Assert.Equal(["compression", "core", "text"], projects.Select(project => project.GetProperty("projectKey").GetString()));
        var compressionSummary = projects[0];
        Assert.True(compressionSummary.GetProperty("completeInventoryKnown").GetBoolean());
        Assert.Equal(2, compressionSummary.GetProperty("plannedCaseCount").GetInt32());
        Assert.Equal(1, compressionSummary.GetProperty("completedCaseCount").GetInt32());
        Assert.Equal(1, compressionSummary.GetProperty("neverStartedCaseCount").GetInt32());
        Assert.Equal("00:00:02", compressionSummary.GetProperty("buildDuration").GetString());
        Assert.Equal("00:00:05", compressionSummary.GetProperty("runDuration").GetString());

        foreach (var unstarted in projects.Skip(1))
        {
            Assert.False(unstarted.GetProperty("completeInventoryKnown").GetBoolean());
            Assert.Equal(JsonValueKind.Null, unstarted.GetProperty("plannedCaseCount").ValueKind);
            Assert.Equal("never-started", unstarted.GetProperty("state").GetString());
        }
    }

    private static BenchmarkExecutionPlan CreateSelectorPlan(string suite)
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var options = new BenchmarkCommandOptions(suite, "fast", "net10.0", 0, "synthetic",
            null, null, null, null, null, null, false, false, false, false, 200, false, false, []);
        return BenchmarkCommandPlanner.Create(options, configuration, checkout.RepositoryRoot);
    }

    private static BenchmarkExecutionPlan CreatePlan()
        => new("core", "exhaustive", "net11.0", "synthetic", 100_000, false, null,
            [new BenchmarkInvocation("core", "src/devops/benchmarks/Rowles.LeanCorpus.Benchmarks/Rowles.LeanCorpus.Benchmarks.csproj", [], "ExampleBenchmarks")], [], []);

    private static BenchmarkExecutionPlan CreateUnfilteredPlan()
        => new("core", "fast", "net10.0", "synthetic", 500, false, null,
            [new BenchmarkInvocation("core", "src/devops/benchmarks/Rowles.LeanCorpus.Benchmarks/Rowles.LeanCorpus.Benchmarks.csproj", [], null)], [], []);

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Benchmark case coverage " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}

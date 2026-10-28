using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Data;

internal sealed record DataForgeQualificationOptions(string ReferencePath, bool Dry);

internal sealed record DataForgeQualificationParseResult(
    bool Success,
    DataForgeQualificationOptions? Options,
    string? Failure);

internal static class DataForgeQualificationArgumentParser
{
    public static DataForgeQualificationParseResult Parse(IReadOnlyList<string> arguments, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        string? referencePath = null;
        var dry = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument.Equals("-Dry", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--dry", StringComparison.OrdinalIgnoreCase))
            {
                dry = true;
                continue;
            }

            if (argument.Equals("-ReferencePath", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--reference-path", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
                    return Invalid("ReferencePath requires a non-empty path.");
                referencePath = arguments[index];
                continue;
            }

            return Invalid($"Unexpected DataForge qualification argument '{argument}'.");
        }

        var resolvedReferencePath = Path.GetFullPath(Path.IsPathRooted(referencePath ?? string.Empty)
            ? referencePath!
            : Path.Combine(repositoryRoot, referencePath ?? Path.Combine("artifacts", "dataforge", "reference", "leancorpus-wikipedia-en-v1")));
        return new DataForgeQualificationParseResult(true,
            new DataForgeQualificationOptions(resolvedReferencePath, dry), null);
    }

    private static DataForgeQualificationParseResult Invalid(string message)
        => new(false, null, $"{message}{Environment.NewLine}Usage: dataforge qualify [--reference-path <path>] [--dry]");
}

internal enum DataForgeQualificationStepKind
{
    Build,
    ProfileVerification,
    ReferenceVerification,
    Test,
    Benchmark
}

internal sealed record DataForgeQualificationStep(
    string Group,
    string Name,
    string Command,
    DataForgeQualificationStepKind Kind,
    IReadOnlyList<string> Arguments,
    string? Suite = null,
    string? Framework = null,
    string? Strategy = null,
    string Dataset = "synthetic",
    IReadOnlyList<string>? BenchmarkArguments = null);

internal sealed record DataForgeQualificationPlan(IReadOnlyList<DataForgeQualificationStep> Steps)
{
    public static DataForgeQualificationPlan Create(DataForgeQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var steps = new List<DataForgeQualificationStep>
        {
            new("build", "Solution build", "./devops build", DataForgeQualificationStepKind.Build, ["build"]),
            new("profiles", "Verify required DataForge profiles", "./devops dataforge profiles -Json",
                DataForgeQualificationStepKind.ProfileVerification, ["profiles", "-Json"]),
            new("wikipedia-reference", "Verify frozen Wikipedia reference",
                Command("dataforge", ["reference", "verify", "-ReferencePath", options.ReferencePath]),
                DataForgeQualificationStepKind.ReferenceVerification,
                ["reference", "verify", "-ReferencePath", options.ReferencePath])
        };

        AddTest(steps, "DataForge net10.0", "dataforge", "net10.0");
        AddTest(steps, "DataForge net11.0", "dataforge", "net11.0");
        AddTest(steps, "Architecture net10.0", "architecture", "net10.0");
        AddTest(steps, "Server integration net11.0", "server-integration", "net11.0");

        AddBenchmark(steps, "synthetic-benchmarks", "Term search smoke", "query", "fast");
        AddBenchmark(steps, "synthetic-benchmarks", "HNSW smoke", "hnsw", "fast");
        AddBenchmark(steps, "synthetic-benchmarks", "Vector quantisation smoke", "vq", "fast");
        AddBenchmark(steps, "synthetic-benchmarks", "Hybrid search smoke", "hybrid", "fast");
        AddBenchmark(steps, "synthetic-benchmarks", "Rowles.Text multilingual smoke", "text", "fast",
            benchmarkArguments: ["--filter", "*MultiLanguageAnalysisBenchmarks*"]);

        foreach (var (suite, name) in new[]
        {
            ("index", "Wikipedia indexing smoke"),
            ("query", "Wikipedia term search smoke"),
            ("phrase", "Wikipedia phrase search smoke"),
            ("prefix", "Wikipedia prefix search smoke"),
            ("wildcard", "Wikipedia wildcard search smoke"),
            ("regexp", "Wikipedia regexp search smoke")
        })
        {
            AddBenchmark(steps, "wikipedia-smoke", name, suite, "fast", "wikipedia", options.ReferencePath);
        }

        foreach (var (suite, name) in new[]
        {
            ("index", "Wikipedia indexing"),
            ("query", "Wikipedia term search"),
            ("phrase", "Wikipedia phrase search"),
            ("terminset", "Wikipedia multi-term search"),
            ("highlighter", "Wikipedia highlighting"),
            ("merge", "Wikipedia merge")
        })
        {
            AddBenchmark(steps, "wikipedia-benchmarks", name, suite, "default", "wikipedia", options.ReferencePath);
        }

        return new DataForgeQualificationPlan(steps);
    }

    public IReadOnlyList<string> Validate(DevOpsConfiguration configuration, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var failures = new List<string>();
        try
        {
            _ = RepositoryProjectCatalog.GetExistingProjectPath(repositoryRoot, RepositoryProjectRole.DataForgeTool);
        }
        catch (FileNotFoundException exception)
        {
            failures.Add(exception.Message);
        }
        foreach (var step in Steps)
        {
            if (step.Kind == DataForgeQualificationStepKind.Test)
            {
                if (string.IsNullOrWhiteSpace(step.Suite) || !configuration.TestSuites.TryGetValue(step.Suite, out var testSuite))
                {
                    failures.Add($"Qualification step '{step.Name}' references unknown test suite '{step.Suite}'.");
                    continue;
                }
                if (!testSuite.DataForgeQualification)
                    failures.Add($"Qualification test suite '{step.Suite}' is not marked for DataForge qualification.");
                if (!testSuite.Runner.Equals("Mtp", StringComparison.Ordinal))
                    failures.Add($"Qualification test suite '{step.Suite}' must use the Mtp runner.");
                if (string.IsNullOrWhiteSpace(step.Framework) || !testSuite.Frameworks.Contains(step.Framework, StringComparer.OrdinalIgnoreCase))
                    failures.Add($"Test suite '{step.Suite}' does not support qualification framework '{step.Framework}'.");
                continue;
            }

            if (step.Kind != DataForgeQualificationStepKind.Benchmark)
                continue;

            if (string.IsNullOrWhiteSpace(step.Suite))
            {
                failures.Add($"Qualification step '{step.Name}' requires a benchmark suite or project selector.");
                continue;
            }

            configuration.BenchmarkSuites.TryGetValue(step.Suite, out var benchmarkSuite);
            if (benchmarkSuite?.IsSelector == true)
            {
                failures.Add($"Qualification step '{step.Name}' references non-runnable benchmark selector '{step.Suite}'.");
                continue;
            }
            var projectId = benchmarkSuite?.Project ?? step.Suite;
            if (!configuration.BenchmarkProjects.TryGetValue(projectId, out var benchmarkProject))
            {
                failures.Add($"Qualification step '{step.Name}' references unknown benchmark suite or project '{step.Suite}'.");
                continue;
            }
            if (!configuration.BenchmarkStrategies.ContainsKey(step.Strategy ?? string.Empty))
                failures.Add($"Qualification benchmark step '{step.Name}' references unknown strategy '{step.Strategy}'.");
            if (string.IsNullOrWhiteSpace(step.Framework) || !benchmarkProject.Frameworks.Contains(step.Framework, StringComparer.OrdinalIgnoreCase))
                failures.Add($"Benchmark project '{projectId}' does not support qualification framework '{step.Framework}'.");
            if (benchmarkSuite is not null && !benchmarkSuite.Frameworks.IsDefaultOrEmpty && !benchmarkSuite.Frameworks.Contains(step.Framework ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                failures.Add($"Benchmark suite '{step.Suite}' does not support qualification framework '{step.Framework}'.");
            if (benchmarkSuite is not null && !benchmarkSuite.Datasets.IsDefaultOrEmpty && !benchmarkSuite.Datasets.Contains(step.Dataset, StringComparer.OrdinalIgnoreCase))
                failures.Add($"Benchmark suite '{step.Suite}' does not support qualification dataset '{step.Dataset}'.");
            if (!step.Dataset.Equals(configuration.DefaultBenchmarkDataset, StringComparison.OrdinalIgnoreCase) &&
                (!configuration.BenchmarkDatasets.TryGetValue(step.Dataset, out var eligibleSuites) ||
                 !eligibleSuites.Contains(step.Suite, StringComparer.Ordinal)))
                failures.Add($"Qualification benchmark suite '{step.Suite}' is not eligible for dataset '{step.Dataset}'.");
            if (step.Dataset.Equals("wikipedia", StringComparison.OrdinalIgnoreCase) &&
                !step.Arguments.Contains("-ReferencePath", StringComparer.OrdinalIgnoreCase))
                failures.Add($"Qualification benchmark step '{step.Name}' is missing its frozen Wikipedia reference path.");
        }

        return failures;
    }

    private static void AddTest(List<DataForgeQualificationStep> steps, string name, string suite, string framework)
    {
        var arguments = new[] { "test", "-Suite", suite, "-Framework", framework, "-CI" };
        steps.Add(new DataForgeQualificationStep("synthetic-tests", name, Command("test", arguments.Skip(1)),
            DataForgeQualificationStepKind.Test, arguments, suite, framework));
    }

    private static void AddBenchmark(
        List<DataForgeQualificationStep> steps,
        string group,
        string name,
        string suite,
        string strategy,
        string dataset = "synthetic",
        string? referencePath = null,
        IReadOnlyList<string>? benchmarkArguments = null)
    {
        var arguments = new List<string> { "benchmark", "-Suite", suite, "-Strat", strategy };
        if (dataset != "synthetic")
        {
            arguments.AddRange(["-Dataset", dataset, "-ReferencePath", referencePath ?? string.Empty]);
        }
        if (benchmarkArguments is { Count: > 0 })
        {
            arguments.Add("--");
            arguments.AddRange(benchmarkArguments);
        }
        steps.Add(new DataForgeQualificationStep(group, name, Command("benchmark", arguments.Skip(1)),
            DataForgeQualificationStepKind.Benchmark, arguments, suite, "net11.0", strategy, dataset, benchmarkArguments));
    }

    private static string Command(string command, IEnumerable<string> arguments)
        => "./devops " + command + " " + string.Join(" ", arguments.Select(Quote));

    private static string Quote(string value)
        => value.Any(char.IsWhiteSpace) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;
}

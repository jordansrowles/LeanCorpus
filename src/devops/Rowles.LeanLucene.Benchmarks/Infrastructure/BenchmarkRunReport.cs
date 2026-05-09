using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rowles.LeanLucene.Benchmarks;

internal sealed class BenchmarkRunReport
{
    public int SchemaVersion { get; set; } = 2;
    public string RunId { get; set; } = string.Empty;
    public string RunType { get; set; } = "full";
    public string GeneratedAtUtc { get; set; } = string.Empty;
    public string[] CommandLineArgs { get; set; } = [];
    public string HostMachineName { get; set; } = Environment.MachineName;
    public string CommitHash { get; set; } = string.Empty;
    public string DotnetVersion { get; set; } = Environment.Version.ToString();
    public BenchmarkProvenanceReport? Provenance { get; set; }
    public int TotalBenchmarkCount { get; set; }
    public List<BenchmarkSuiteReport> Suites { get; set; } = [];
}

internal sealed class BenchmarkProvenanceReport
{
    public string SourceCommit { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public string SourceManifestPath { get; set; } = string.Empty;
    public string GitCommitHash { get; set; } = string.Empty;
    public bool GitAvailable { get; set; }
    public bool? GitDirty { get; set; }
    public string BenchmarkDotNetVersion { get; set; } = string.Empty;
    public string RuntimeFramework { get; set; } = RuntimeInformation.FrameworkDescription;
    public string RuntimeIdentifier { get; set; } = RuntimeInformation.RuntimeIdentifier;
    public string OSDescription { get; set; } = RuntimeInformation.OSDescription;
    public string ProcessArchitecture { get; set; } = RuntimeInformation.ProcessArchitecture.ToString();
    public int? EffectiveDocCount { get; set; }
    public string DataFingerprintSha256 { get; set; } = string.Empty;
    public BenchmarkDataSourceReport[] DataSources { get; set; } = [];
}

internal sealed class BenchmarkDataSourceReport
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long ByteCount { get; set; }
    public int DocumentCount { get; set; }
    public string FingerprintSha256 { get; set; } = string.Empty;
    public bool FallbackUsed { get; set; }
}

internal sealed class BenchmarkSuiteReport
{
    public string SuiteName { get; set; } = string.Empty;
    public string SummaryTitle { get; set; } = string.Empty;
    public int BenchmarkCount { get; set; }
    public List<BenchmarkCaseReport> Benchmarks { get; set; } = [];
}

internal sealed class BenchmarkCaseReport
{
    public string Key { get; set; } = string.Empty;
    public string DisplayInfo { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
    public BenchmarkStatisticsReport? Statistics { get; set; }
    public BenchmarkGcReport? Gc { get; set; }
}

internal sealed class BenchmarkStatisticsReport
{
    public int? SampleCount { get; set; }
    public double? MeanNanoseconds { get; set; }
    public double? MedianNanoseconds { get; set; }
    public double? MinNanoseconds { get; set; }
    public double? MaxNanoseconds { get; set; }
    public double? StandardDeviationNanoseconds { get; set; }
    public double? OperationsPerSecond { get; set; }
}

internal sealed class BenchmarkGcReport
{
    public double? BytesAllocatedPerOperation { get; set; }
    public double? Gen0Collections { get; set; }
    public double? Gen1Collections { get; set; }
    public double? Gen2Collections { get; set; }
}

internal sealed class BenchmarkRunIndex
{
    public int SchemaVersion { get; set; } = 2;
    public List<BenchmarkRunIndexEntry> Runs { get; set; } = [];
}

internal sealed class BenchmarkRunIndexEntry
{
    public string RunId { get; set; } = string.Empty;
    public string RunType { get; set; } = "full";
    public string GeneratedAtUtc { get; set; } = string.Empty;
    public string CommitHash { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public int BenchmarkCount { get; set; }
    public string[] Suites { get; set; } = [];
    public string DataFingerprintSha256 { get; set; } = string.Empty;
    public string Toolchain { get; set; } = string.Empty;
}

internal static class BenchmarkRunReportBuilder
{
    public static BenchmarkRunReport Build(
        string runId,
        DateTimeOffset generatedAtUtc,
        string[] benchmarkArgs,
        IReadOnlyList<(string Suite, Summary Summary)> suiteSummaries)
    {
        var suiteReports = suiteSummaries
            .Select(BuildSuiteReport)
            .OrderBy(r => r.SuiteName, StringComparer.Ordinal)
            .ToList();

        return new BenchmarkRunReport
        {
            RunId = runId,
            GeneratedAtUtc = generatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            CommandLineArgs = benchmarkArgs,
            TotalBenchmarkCount = suiteReports.Sum(r => r.BenchmarkCount),
            Suites = suiteReports
        };
    }

    private static BenchmarkSuiteReport BuildSuiteReport((string Suite, Summary Summary) source)
    {
        // BDN emits two BenchmarkReport entries per case (full trace + converged result).
        // Keep the lower-mean entry, which is the converged measurement after outlier removal.
        var benchmarks = source.Summary.Reports
            .Select(BuildCaseReport)
            .GroupBy(r => r.Key, StringComparer.Ordinal)
            .Select(g => g.MinBy(r => r.Statistics?.MeanNanoseconds ?? double.MaxValue)!)
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .ToList();

        return new BenchmarkSuiteReport
        {
            SuiteName = source.Suite,
            SummaryTitle = source.Summary.Title,
            BenchmarkCount = benchmarks.Count,
            Benchmarks = benchmarks
        };
    }

    private static BenchmarkCaseReport BuildCaseReport(BenchmarkReport report)
    {
        var benchmarkCase = report.BenchmarkCase;
        var descriptor = benchmarkCase.Descriptor;
        var parameters = benchmarkCase.Parameters.Items
            .ToDictionary(
                item => item.Name,
                item => item.Value?.ToString() ?? string.Empty,
                StringComparer.Ordinal);

        var key = BuildBenchmarkKey(descriptor.Type.Name, descriptor.WorkloadMethod.Name, parameters);

        return new BenchmarkCaseReport
        {
            Key = key,
            DisplayInfo = benchmarkCase.DisplayInfo,
            TypeName = descriptor.Type.Name,
            MethodName = descriptor.WorkloadMethod.Name,
            Parameters = parameters,
            Statistics = BuildStatisticsReport(report.ResultStatistics),
            Gc = BuildGcReport(report.GcStats, report.Metrics)
        };
    }

    private static string BuildBenchmarkKey(
        string typeName,
        string methodName,
        IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters.Count == 0)
        {
            return $"{typeName}.{methodName}";
        }

        var parameterParts = parameters
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => $"{kvp.Key}={kvp.Value}");

        return $"{typeName}.{methodName}|{string.Join(", ", parameterParts)}";
    }

    private static BenchmarkStatisticsReport? BuildStatisticsReport(object? statistics)
    {
        if (statistics is null)
        {
            return null;
        }

        var mean = ReadDoubleProperty(statistics, "Mean");

        return new BenchmarkStatisticsReport
        {
            SampleCount = ReadIntProperty(statistics, "N"),
            MeanNanoseconds = mean,
            MedianNanoseconds = ReadDoubleProperty(statistics, "Median"),
            MinNanoseconds = ReadDoubleProperty(statistics, "Min"),
            MaxNanoseconds = ReadDoubleProperty(statistics, "Max"),
            StandardDeviationNanoseconds = ReadDoubleProperty(statistics, "StandardDeviation"),
            OperationsPerSecond = mean is > 0 ? 1_000_000_000d / mean.Value : null
        };
    }

    private static BenchmarkGcReport? BuildGcReport(GcStats gcStats, IReadOnlyDictionary<string, Metric>? metrics)
    {
        // The MemoryDiagnoser stores allocation in report.Metrics under "Allocated Memory".
        // GcStats.BytesAllocatedPerOperation is null at runtime even when [MemoryDiagnoser] is active.
        double? bytesAllocated = null;
        if (metrics is not null && metrics.TryGetValue("Allocated Memory", out var allocMetric))
            bytesAllocated = allocMetric.Value;

        return new BenchmarkGcReport
        {
            BytesAllocatedPerOperation = bytesAllocated,
            Gen0Collections = gcStats.Gen0Collections,
            Gen1Collections = gcStats.Gen1Collections,
            Gen2Collections = gcStats.Gen2Collections
        };
    }

    private static double? ReadDoubleProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance);

        if (property is null)
        {
            return null;
        }

        var value = property.GetValue(instance);
        if (value is null)
        {
            return null;
        }

        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private static int? ReadIntProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance);

        if (property is null)
        {
            return null;
        }

        var value = property.GetValue(instance);
        if (value is null)
        {
            return null;
        }

        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }
}

internal static class BenchmarkProvenanceBuilder
{
    public static BenchmarkProvenanceReport Build(string repoRoot, string gitCommitHash, int? effectiveDocCount)
    {
        var dataSources = BenchmarkData.GetLoadedDataSources();
        var sourceCommit = FirstNonEmpty(
            Environment.GetEnvironmentVariable("BENCH_SOURCE_COMMIT"),
            gitCommitHash);

        return new BenchmarkProvenanceReport
        {
            SourceCommit = sourceCommit,
            SourceRef = Environment.GetEnvironmentVariable("BENCH_SOURCE_REF") ?? string.Empty,
            SourceManifestPath = Environment.GetEnvironmentVariable("BENCH_SOURCE_MANIFEST") ?? string.Empty,
            GitCommitHash = gitCommitHash,
            GitAvailable = Directory.Exists(Path.Combine(repoRoot, ".git")),
            GitDirty = TryReadGitDirty(repoRoot),
            BenchmarkDotNetVersion = GetBenchmarkDotNetVersion(),
            EffectiveDocCount = effectiveDocCount,
            DataSources = dataSources,
            DataFingerprintSha256 = BuildCombinedFingerprint(dataSources)
        };
    }

    private static string GetBenchmarkDotNetVersion()
    {
        var assembly = typeof(BenchmarkRunner).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? string.Empty;
    }

    private static string BuildCombinedFingerprint(BenchmarkDataSourceReport[] dataSources)
    {
        if (dataSources.Length == 0)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var source in dataSources.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            builder
                .Append(source.Name).Append('\0')
                .Append(source.FingerprintSha256).Append('\0')
                .Append(source.DocumentCount.ToString(CultureInfo.InvariantCulture)).Append('\0');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static bool? TryReadGitDirty(string repoRoot)
    {
        if (!Directory.Exists(Path.Combine(repoRoot, ".git")))
            return null;

        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "status --porcelain",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? !string.IsNullOrWhiteSpace(output) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

internal static class BenchmarkRunReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void WriteReport(string runDir, string machineDir, BenchmarkRunReport report)
    {
        Directory.CreateDirectory(runDir);

        var reportPath = Path.Combine(runDir, "report.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions));

        Directory.CreateDirectory(machineDir);
        var indexPath = Path.Combine(machineDir, "index.json");
        var runIndex = File.Exists(indexPath)
            ? JsonSerializer.Deserialize<BenchmarkRunIndex>(File.ReadAllText(indexPath), JsonOptions) ?? new BenchmarkRunIndex()
            : new BenchmarkRunIndex();

        runIndex.Runs.RemoveAll(entry => string.Equals(entry.RunId, report.RunId, StringComparison.Ordinal));

        runIndex.Runs.Add(new BenchmarkRunIndexEntry
        {
            RunId = report.RunId,
            RunType = report.RunType,
            GeneratedAtUtc = report.GeneratedAtUtc,
            CommitHash = report.CommitHash,
            File = Path.GetRelativePath(machineDir, reportPath),
            BenchmarkCount = report.TotalBenchmarkCount,
            Suites = report.Suites.Select(s => s.SuiteName).ToArray(),
            DataFingerprintSha256 = report.Provenance?.DataFingerprintSha256 ?? string.Empty,
            Toolchain = report.Provenance?.BenchmarkDotNetVersion ?? string.Empty
        });

        runIndex.Runs = runIndex.Runs
            .OrderByDescending(entry => entry.GeneratedAtUtc, StringComparer.Ordinal)
            .ToList();

        File.WriteAllText(indexPath, JsonSerializer.Serialize(runIndex, JsonOptions));
    }
}

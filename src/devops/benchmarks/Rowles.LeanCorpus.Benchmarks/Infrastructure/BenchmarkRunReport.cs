using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.DataForge;

namespace Rowles.LeanCorpus.Benchmarks;

internal sealed class BenchmarkRunReport
{
    public int SchemaVersion { get; set; } = 5;
    public string RunId { get; set; } = string.Empty;
    public string RunType { get; set; } = "full";
    public string GeneratedAtUtc { get; set; } = string.Empty;
    public string[] CommandLineArgs { get; set; } = [];
    public string HostMachineName { get; set; } = Environment.MachineName;
    public string CommitHash { get; set; } = string.Empty;
    public string DotnetVersion { get; set; } = Environment.Version.ToString();
    public BenchmarkProvenanceReport? Provenance { get; set; }
    public int RegisteredBenchmarkCount { get; set; }
    public int TotalBenchmarkCount { get; set; }
    public List<string> QualityFlags { get; set; } = [];
    public List<GutenbergPersistenceVerification> PersistenceVerifications { get; set; } = [];
    public List<SuggesterQualityEvidence> SuggesterQuality { get; set; } = [];
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
    public bool RscriptAvailable { get; set; }
    public int? EffectiveDocCount { get; set; }
    public string DataFingerprintSha256 { get; set; } = string.Empty;
    public BenchmarkDatasetReport[] Datasets { get; set; } = [];
    public BenchmarkCorpusReport? Corpus { get; set; }
}

internal sealed class BenchmarkDatasetReport
{
    public string SourceKind { get; set; } = string.Empty;
    public int DataForgeVersion { get; set; }
    public string? ProfileId { get; set; }
    public int? ProfileVersion { get; set; }
    public string? DatasetId { get; set; }
    public int? DatasetVersion { get; set; }
    public ulong? Seed { get; set; }
    public int RecordCount { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
    public DataForgeDependencyVersion[] Dependencies { get; set; } = [];
    public string ContentSha256 { get; set; } = string.Empty;
}

internal sealed class BenchmarkCorpusReport
{
    public int DocumentCount { get; set; }
    public long Utf8ByteCount { get; set; }
    public long TokenCount { get; set; }
    public int UniqueTermCount { get; set; }
    public string PayloadSha256 { get; set; } = string.Empty;
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
    public int RegisteredBenchmarkCount { get; set; }
    public int BenchmarkCount { get; set; }
    public int MeasuredBenchmarkCount { get; set; }
    public int FailedBenchmarkCount { get; set; }
    public int IntermediateBenchmarkCount { get; set; }
    public int MissingBenchmarkCount { get; set; }
    public List<string> QualityFlags { get; set; } = [];
    public List<BenchmarkCaseReport> Benchmarks { get; set; } = [];
}

internal sealed class BenchmarkCaseReport
{
    public string Key { get; set; } = string.Empty;
    public string DisplayInfo { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
    public string CaseIdentitySha256 { get; set; } = string.Empty;
    public string ResultRowId { get; set; } = string.Empty;
    public string ResultState { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string ResolvedJobId { get; set; } = string.Empty;
    public string JobDisplayInfo { get; set; } = string.Empty;
    public string RuntimeName { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public string ToolchainType { get; set; } = string.Empty;
    public string ToolchainDisplayInfo { get; set; } = string.Empty;
    public string ToolchainRuntimeName { get; set; } = string.Empty;
    public string ToolchainRuntimeVersion { get; set; } = string.Empty;
    public bool ToolchainIsInProcess { get; set; }
    public bool IdentityComplete { get; set; }
    public bool PreflightPassed { get; set; }
    public Dictionary<string, string> JobSettings { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new(StringComparer.Ordinal);
    public int MeasurementCount { get; set; }
    public int ActualWorkloadMeasurementCount { get; set; }
    public string MeasurementFingerprintSha256 { get; set; } = string.Empty;
    public List<string> QualityFlags { get; set; } = [];
    public BenchmarkStatisticsReport? Statistics { get; set; }
    public BenchmarkGcReport? Gc { get; set; }
    internal string ResultFingerprintSha256 { get; set; } = string.Empty;
}

internal sealed record BenchmarkCaseIdentitySource(
    string TypeName,
    string MethodName,
    string DisplayInfo,
    IReadOnlyDictionary<string, string> Parameters,
    string JobId,
    string JobResolvedId,
    string JobDisplayInfo,
    string RuntimeName,
    string RuntimeVersion,
    string ToolchainType,
    string ToolchainDisplayInfo,
    string ToolchainRuntimeName,
    string ToolchainRuntimeVersion,
    bool ToolchainIsInProcess,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, string> EnvironmentVariables);

internal sealed record BenchmarkCaseIdentityReport(
    string Key,
    string IdentitySha256,
    string TypeName,
    string MethodName,
    string DisplayInfo,
    Dictionary<string, string> Parameters,
    string JobId,
    string JobResolvedId,
    string JobDisplayInfo,
    string RuntimeName,
    string RuntimeVersion,
    string ToolchainType,
    string ToolchainDisplayInfo,
    string ToolchainRuntimeName,
    string ToolchainRuntimeVersion,
    bool ToolchainIsInProcess,
    bool IdentityComplete,
    Dictionary<string, string> Settings,
    Dictionary<string, string> EnvironmentVariables);

internal sealed class BenchmarkStatisticsReport
{
    public int? SampleCount { get; set; }
    public double? MeanNanoseconds { get; set; }
    public double? MedianNanoseconds { get; set; }
    public double? MinNanoseconds { get; set; }
    public double? MaxNanoseconds { get; set; }
    public double? StandardDeviationNanoseconds { get; set; }
    public double? CoefficientOfVariationPercent { get; set; }
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
    public int SchemaVersion { get; set; } = 4;
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
    public int RegisteredBenchmarkCount { get; set; }
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
        bool includesGutenbergPersistence = suiteSummaries
            .SelectMany(static source => source.Summary.BenchmarksCases)
            .Any(static benchmarkCase => benchmarkCase.Descriptor.Type == typeof(GutenbergIndexingBenchmarks));

        return new BenchmarkRunReport
        {
            RunId = runId,
            GeneratedAtUtc = generatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            CommandLineArgs = benchmarkArgs,
            RegisteredBenchmarkCount = suiteReports.Sum(r => r.RegisteredBenchmarkCount),
            TotalBenchmarkCount = suiteReports.Sum(r => r.BenchmarkCount),
            QualityFlags = suiteReports
                .SelectMany(r => r.QualityFlags)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static flag => flag, StringComparer.Ordinal)
                .ToList(),
            PersistenceVerifications = includesGutenbergPersistence
                ? GutenbergPersistenceFixtureVerifier.Verify(BenchmarkHelpers.RepositoryRoot).ToList()
                : [],
            Suites = suiteReports
        };
    }

    private static BenchmarkSuiteReport BuildSuiteReport((string Suite, Summary Summary) source)
    {
        var benchmarks = PreserveReportRows(source.Summary.Reports.Select(BuildCaseReport));

        var registeredKeys = source.Summary.BenchmarksCases
            .Select(static benchmarkCase => CreateCaseIdentity(BuildIdentitySource(benchmarkCase)).Key)
            .ToHashSet(StringComparer.Ordinal);
        var measuredKeys = benchmarks
            .Where(static benchmark => benchmark.ResultState == "TerminalResult")
            .Select(static benchmark => benchmark.Key)
            .ToHashSet(StringComparer.Ordinal);
        int failedCount = benchmarks.Count(static benchmark => benchmark.ResultState == "Failed");
        int intermediateCount = benchmarks.Count(static benchmark => benchmark.ResultState is "Intermediate" or "InvalidStatistics");
        int missingCount = registeredKeys.Except(measuredKeys, StringComparer.Ordinal).Count();
        var qualityFlags = new List<string>();
        if (failedCount > 0)
            qualityFlags.Add("FailedBenchmarks");
        if (missingCount > 0)
            qualityFlags.Add("MissingBenchmarks");
        if (intermediateCount > 0)
            qualityFlags.Add("NonTerminalBenchmarkReports");
        if (benchmarks.Any(static benchmark => benchmark.ResultState == "InvalidStatistics"))
            qualityFlags.Add("InvalidTerminalStatistics");
        if (benchmarks.Any(static benchmark => !benchmark.IdentityComplete))
            qualityFlags.Add("IncompleteBenchmarkIdentity");
        if (benchmarks.Any(static benchmark => benchmark.QualityFlags.Contains("Noisy")))
            qualityFlags.Add("NoisyMeasurements");
        if (benchmarks.Any(static benchmark => benchmark.QualityFlags.Contains("InsufficientSamples")))
            qualityFlags.Add("InsufficientSamples");
        if (benchmarks.Any(static benchmark => benchmark.QualityFlags.Any(IsComparisonCaveat)))
            qualityFlags.Add("ComparisonCaveats");

        return new BenchmarkSuiteReport
        {
            SuiteName = source.Suite,
            SummaryTitle = source.Summary.Title,
            RegisteredBenchmarkCount = registeredKeys.Count,
            BenchmarkCount = benchmarks.Count,
            MeasuredBenchmarkCount = measuredKeys.Count,
            FailedBenchmarkCount = failedCount,
            IntermediateBenchmarkCount = intermediateCount,
            MissingBenchmarkCount = missingCount,
            QualityFlags = qualityFlags,
            Benchmarks = benchmarks
        };
    }

    private static BenchmarkCaseReport BuildCaseReport(BenchmarkReport report)
    {
        BenchmarkCaseIdentityReport identity = CreateCaseIdentity(BuildIdentitySource(report.BenchmarkCase));
        var statistics = BuildStatisticsReport(report.ResultStatistics);
        return BuildCaseReport(identity, report.Success, report.AllMeasurements, statistics, BuildGcReport(report.GcStats, report.Metrics));
    }

    internal static BenchmarkCaseReport BuildCaseReport(
        BenchmarkCaseIdentityReport identity,
        bool success,
        IReadOnlyList<Measurement> measurements,
        BenchmarkStatisticsReport? statistics,
        BenchmarkGcReport? gc)
    {
        string resultState = ClassifyResultState(success, statistics, measurements);
        BenchmarkStatisticsReport? terminalStatistics = resultState == "TerminalResult" ? statistics : null;
        var orderedMeasurements = measurements
            .OrderBy(static measurement => measurement.LaunchIndex)
            .ThenBy(static measurement => measurement.IterationMode)
            .ThenBy(static measurement => measurement.IterationStage)
            .ThenBy(static measurement => measurement.IterationIndex)
            .Select(static measurement => new
            {
                iterationMode = measurement.IterationMode.ToString(),
                iterationStage = measurement.IterationStage.ToString(),
                launchIndex = measurement.LaunchIndex,
                iterationIndex = measurement.IterationIndex,
                operations = measurement.Operations,
                nanoseconds = measurement.Nanoseconds
            })
            .ToArray();
        string measurementFingerprint = Sha256(JsonSerializer.SerializeToUtf8Bytes(orderedMeasurements));
        int actualWorkloadMeasurementCount = measurements.Count(static measurement =>
            measurement.IterationMode == IterationMode.Workload && measurement.IterationStage == IterationStage.Actual);
        string resultFingerprint = Sha256(JsonSerializer.SerializeToUtf8Bytes(new
        {
            identity = identity.IdentitySha256,
            resultState,
            success,
            measurementFingerprint,
            statistics = terminalStatistics,
            gc
        }));
        var qualityFlags = resultState switch
        {
            "Failed" => new List<string> { "Failed" },
            "Intermediate" => new List<string> { "IntermediateResult" },
            "InvalidStatistics" => new List<string> { "InvalidTerminalStatistics" },
            _ => BuildQualityFlags(identity.TypeName.Split('.').Last(), identity.MethodName, identity.Parameters, terminalStatistics)
        };

        return new BenchmarkCaseReport
        {
            Key = identity.Key,
            DisplayInfo = identity.DisplayInfo,
            TypeName = identity.TypeName,
            MethodName = identity.MethodName,
            Parameters = identity.Parameters,
            CaseIdentitySha256 = identity.IdentitySha256,
            ResultState = resultState,
            JobId = identity.JobId,
            ResolvedJobId = identity.JobResolvedId,
            JobDisplayInfo = identity.JobDisplayInfo,
            RuntimeName = identity.RuntimeName,
            RuntimeVersion = identity.RuntimeVersion,
            ToolchainType = identity.ToolchainType,
            ToolchainDisplayInfo = identity.ToolchainDisplayInfo,
            ToolchainRuntimeName = identity.ToolchainRuntimeName,
            ToolchainRuntimeVersion = identity.ToolchainRuntimeVersion,
            ToolchainIsInProcess = identity.ToolchainIsInProcess,
            IdentityComplete = identity.IdentityComplete,
            PreflightPassed = resultState == "TerminalResult",
            JobSettings = identity.Settings,
            EnvironmentVariables = identity.EnvironmentVariables,
            MeasurementCount = measurements.Count,
            ActualWorkloadMeasurementCount = actualWorkloadMeasurementCount,
            MeasurementFingerprintSha256 = measurementFingerprint,
            ResultFingerprintSha256 = resultFingerprint,
            QualityFlags = qualityFlags,
            Statistics = terminalStatistics,
            Gc = resultState == "TerminalResult" ? gc : null
        };
    }

    internal static List<BenchmarkCaseReport> PreserveReportRows(IEnumerable<BenchmarkCaseReport> reports)
    {
        var ordered = reports
            .OrderBy(static report => report.CaseIdentitySha256, StringComparer.Ordinal)
            .ThenBy(static report => report.ResultState, StringComparer.Ordinal)
            .ThenBy(static report => report.ResultFingerprintSha256, StringComparer.Ordinal)
            .ToList();
        var occurrenceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (BenchmarkCaseReport report in ordered)
        {
            string occurrenceKey = $"{report.CaseIdentitySha256}:{report.ResultFingerprintSha256}";
            occurrenceCounts.TryGetValue(occurrenceKey, out int occurrence);
            occurrenceCounts[occurrenceKey] = occurrence + 1;
            report.ResultRowId = Sha256(Encoding.UTF8.GetBytes($"{occurrenceKey}:{occurrence.ToString(CultureInfo.InvariantCulture)}"));
        }

        return ordered;
    }

    internal static string ClassifyResultState(
        bool success,
        BenchmarkStatisticsReport? statistics,
        IReadOnlyList<Measurement> measurements)
    {
        if (!success)
            return "Failed";
        if (!measurements.Any(static measurement =>
                measurement.IterationMode == IterationMode.Workload && measurement.IterationStage == IterationStage.Actual))
            return "Intermediate";
        return HasValidStatistics(statistics) ? "TerminalResult" : "InvalidStatistics";
    }

    internal static bool HasValidStatistics(BenchmarkStatisticsReport? statistics)
    {
        if (statistics is null || statistics.SampleCount is not > 0
            || statistics.MeanNanoseconds is not { } mean || !double.IsFinite(mean) || mean <= 0
            || statistics.MedianNanoseconds is not { } median || !double.IsFinite(median)
            || statistics.MinNanoseconds is not { } min || !double.IsFinite(min)
            || statistics.MaxNanoseconds is not { } max || !double.IsFinite(max)
            || statistics.StandardDeviationNanoseconds is not { } standardDeviation
            || !double.IsFinite(standardDeviation) || standardDeviation < 0)
            return false;

        return min <= median && median <= max
            && min <= mean && mean <= max
            && statistics.CoefficientOfVariationPercent is { } coefficientOfVariation
            && double.IsFinite(coefficientOfVariation) && coefficientOfVariation >= 0
            && statistics.OperationsPerSecond is { } operationsPerSecond
            && double.IsFinite(operationsPerSecond) && operationsPerSecond > 0;
    }

    private static BenchmarkCaseIdentitySource BuildIdentitySource(BenchmarkCase benchmarkCase)
    {
        var descriptor = benchmarkCase.Descriptor;
        Job job = benchmarkCase.Job ?? throw new InvalidOperationException("BenchmarkDotNet benchmark case has no job.");
        Runtime runtime = benchmarkCase.GetRuntime();
        IToolchain toolchain = job.Infrastructure.Toolchain ?? runtime.GetDefaultToolchain(benchmarkCase)
            ?? throw new InvalidOperationException("BenchmarkDotNet benchmark case has no resolved toolchain.");
        Runtime? toolchainRuntime = toolchain.Runtime;
        var parameters = benchmarkCase.Parameters.Items
            .ToDictionary(
                static item => item.Name,
                static item => Convert.ToString(item.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal);
        var environmentVariables = job.Environment.EnvironmentVariables
            .GroupBy(static variable => variable.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last().Value, StringComparer.Ordinal);

        return new BenchmarkCaseIdentitySource(
            descriptor.Type.FullName ?? descriptor.Type.Name,
            descriptor.WorkloadMethod.Name,
            benchmarkCase.DisplayInfo,
            parameters,
            job.Id,
            job.ResolvedId,
            job.DisplayInfo,
            runtime.Name,
            runtime.Version?.ToString() ?? string.Empty,
            toolchain.GetType().FullName ?? toolchain.GetType().Name,
            toolchain.ToString() ?? toolchain.GetType().Name,
            toolchainRuntime?.Name ?? string.Empty,
            toolchainRuntime?.Version?.ToString() ?? string.Empty,
            toolchain.IsInProcess,
            BuildJobSettings(job, toolchain),
            environmentVariables);
    }

    internal static BenchmarkCaseIdentityReport CreateCaseIdentity(BenchmarkCase benchmarkCase)
        => CreateCaseIdentity(BuildIdentitySource(benchmarkCase));

    internal static BenchmarkCaseIdentityReport CreateCaseIdentity(BenchmarkCaseIdentitySource source)
    {
        Dictionary<string, string> parameters = Normalise(source.Parameters);
        Dictionary<string, string> settings = Normalise(source.Settings);
        Dictionary<string, string> environmentVariables = Normalise(source.EnvironmentVariables);
        var canonicalIdentity = new
        {
            typeName = source.TypeName,
            methodName = source.MethodName,
            parameters = ToCanonicalPairs(parameters),
            jobId = source.JobId,
            jobResolvedId = source.JobResolvedId,
            jobDisplayInfo = source.JobDisplayInfo,
            runtimeName = source.RuntimeName,
            runtimeVersion = source.RuntimeVersion,
            toolchainType = source.ToolchainType,
            toolchainDisplayInfo = source.ToolchainDisplayInfo,
            toolchainRuntimeName = source.ToolchainRuntimeName,
            toolchainRuntimeVersion = source.ToolchainRuntimeVersion,
            toolchainIsInProcess = source.ToolchainIsInProcess,
            settings = ToCanonicalPairs(settings),
            environmentVariables = ToCanonicalPairs(environmentVariables)
        };
        string identitySha256 = Sha256(JsonSerializer.SerializeToUtf8Bytes(canonicalIdentity));
        string displayKey = BuildBenchmarkKey(source.TypeName.Split('.').Last(), source.MethodName, parameters);
        bool complete = !string.IsNullOrWhiteSpace(source.TypeName)
            && !string.IsNullOrWhiteSpace(source.MethodName)
            && !string.IsNullOrWhiteSpace(source.JobId)
            && !string.IsNullOrWhiteSpace(source.JobResolvedId)
            && !string.IsNullOrWhiteSpace(source.JobDisplayInfo)
            && !string.IsNullOrWhiteSpace(source.RuntimeName)
            && !string.IsNullOrWhiteSpace(source.RuntimeVersion)
            && !string.IsNullOrWhiteSpace(source.ToolchainType)
            && !string.IsNullOrWhiteSpace(source.ToolchainDisplayInfo)
            && !string.IsNullOrWhiteSpace(source.ToolchainRuntimeName)
            && !string.IsNullOrWhiteSpace(source.ToolchainRuntimeVersion);

        return new BenchmarkCaseIdentityReport(
            $"{displayKey}|case={identitySha256}",
            identitySha256,
            source.TypeName,
            source.MethodName,
            source.DisplayInfo,
            parameters,
            source.JobId,
            source.JobResolvedId,
            source.JobDisplayInfo,
            source.RuntimeName,
            source.RuntimeVersion,
            source.ToolchainType,
            source.ToolchainDisplayInfo,
            source.ToolchainRuntimeName,
            source.ToolchainRuntimeVersion,
            source.ToolchainIsInProcess,
            complete,
            settings,
            environmentVariables);
    }

    private static Dictionary<string, string> BuildJobSettings(Job job, IToolchain toolchain)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["environment.platform"] = FormatSetting(job.Environment.Platform),
            ["environment.jit"] = FormatSetting(job.Environment.Jit),
            ["environment.affinity"] = FormatSetting(job.Environment.Affinity),
            ["environment.powerPlanMode"] = FormatSetting(job.Environment.PowerPlanMode),
            ["environment.largeAddressAware"] = FormatSetting(job.Environment.LargeAddressAware),
            ["gc.server"] = FormatSetting(job.Environment.Gc.Server),
            ["gc.concurrent"] = FormatSetting(job.Environment.Gc.Concurrent),
            ["gc.cpuGroups"] = FormatSetting(job.Environment.Gc.CpuGroups),
            ["gc.force"] = FormatSetting(job.Environment.Gc.Force),
            ["gc.allowVeryLargeObjects"] = FormatSetting(job.Environment.Gc.AllowVeryLargeObjects),
            ["gc.retainVm"] = FormatSetting(job.Environment.Gc.RetainVm),
            ["gc.noAffinitize"] = FormatSetting(job.Environment.Gc.NoAffinitize),
            ["gc.heapAffinitizeMask"] = FormatSetting(job.Environment.Gc.HeapAffinitizeMask),
            ["gc.heapCount"] = FormatSetting(job.Environment.Gc.HeapCount),
            ["run.strategy"] = FormatSetting(job.Run.RunStrategy),
            ["run.launchCount"] = FormatSetting(job.Run.LaunchCount),
            ["run.warmupCount"] = FormatSetting(job.Run.WarmupCount),
            ["run.iterationCount"] = FormatSetting(job.Run.IterationCount),
            ["run.iterationTime"] = FormatSetting(job.Run.IterationTime),
            ["run.invocationCount"] = FormatSetting(job.Run.InvocationCount),
            ["run.unrollFactor"] = FormatSetting(job.Run.UnrollFactor),
            ["run.minIterationCount"] = FormatSetting(job.Run.MinIterationCount),
            ["run.maxIterationCount"] = FormatSetting(job.Run.MaxIterationCount),
            ["run.minWarmupIterationCount"] = FormatSetting(job.Run.MinWarmupIterationCount),
            ["run.maxWarmupIterationCount"] = FormatSetting(job.Run.MaxWarmupIterationCount),
            ["run.memoryRandomization"] = FormatSetting(job.Run.MemoryRandomization),
            ["run.consumeTasksSynchronously"] = FormatSetting(job.Run.ConsumeTasksSynchronously),
            ["run.jitTieringMode"] = FormatSetting(job.Run.JitTieringMode),
            ["infrastructure.runtime"] = FormatSetting(job.Infrastructure.Runtime),
            ["infrastructure.buildConfiguration"] = FormatSetting(job.Infrastructure.BuildConfiguration),
            ["infrastructure.toolchain"] = toolchain.GetType().AssemblyQualifiedName ?? toolchain.GetType().FullName ?? toolchain.GetType().Name,
            ["infrastructure.toolchainRuntime"] = FormatSetting(toolchain.Runtime),
            ["infrastructure.toolchainInProcess"] = FormatSetting(toolchain.IsInProcess),
            ["infrastructure.clock"] = job.Infrastructure.Clock?.GetType().FullName ?? string.Empty,
            ["infrastructure.engineFactory"] = job.Infrastructure.EngineFactory?.GetType().FullName ?? string.Empty,
            ["infrastructure.arguments"] = string.Join("\n", job.Infrastructure.Arguments?.Select(static argument => argument.TextRepresentation) ?? []),
            ["accuracy.maxRelativeError"] = FormatSetting(job.Accuracy.MaxRelativeError),
            ["accuracy.maxAbsoluteError"] = FormatSetting(job.Accuracy.MaxAbsoluteError),
            ["accuracy.minIterationTime"] = FormatSetting(job.Accuracy.MinIterationTime),
            ["accuracy.minInvokeCount"] = FormatSetting(job.Accuracy.MinInvokeCount),
            ["accuracy.evaluateOverhead"] = FormatSetting(job.Accuracy.EvaluateOverhead),
            ["accuracy.outlierMode"] = FormatSetting(job.Accuracy.OutlierMode),
            ["accuracy.analyzeLaunchVariance"] = FormatSetting(job.Accuracy.AnalyzeLaunchVariance),
            ["meta.baseline"] = FormatSetting(job.Meta.Baseline),
            ["meta.categories"] = string.Join(",", job.Meta.Categories.OrderBy(static category => category, StringComparer.Ordinal)),
            ["meta.isMutator"] = FormatSetting(job.Meta.IsMutator),
            ["meta.isDefault"] = FormatSetting(job.Meta.IsDefault)
        };
    }

    private static string FormatSetting(object? value)
    {
        if (value is null)
            return string.Empty;
        if (value is bool boolean)
            return boolean.ToString().ToLowerInvariant();
        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty
            : value.ToString() ?? string.Empty;
    }

    private static Dictionary<string, string> Normalise(IReadOnlyDictionary<string, string> values)
        => values.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

    private static KeyValuePair<string, string>[] ToCanonicalPairs(IReadOnlyDictionary<string, string> values)
        => values.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray();

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

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
            CoefficientOfVariationPercent = mean is > 0
                ? ReadDoubleProperty(statistics, "StandardDeviation") / mean.Value * 100d
                : null,
            OperationsPerSecond = mean is > 0 ? 1_000_000_000d / mean.Value : null
        };
    }

    internal static List<string> BuildQualityFlags(
        string typeName,
        string methodName,
        IReadOnlyDictionary<string, string> parameters,
        BenchmarkStatisticsReport? statistics)
    {
        var flags = new List<string>();
        if (statistics is null)
        {
            flags.Add("Failed");
            return flags;
        }

        if (statistics.CoefficientOfVariationPercent is > 20d)
            flags.Add("Noisy");
        else if (statistics.CoefficientOfVariationPercent is > 10d)
            flags.Add("Directional");

        if (statistics.SampleCount is < 3)
            flags.Add("InsufficientSamples");

        if (methodName.Contains("ColdPerWordAdapter", StringComparison.Ordinal))
            flags.Add("AdapterOverhead");

        if (typeName == nameof(CombinedFieldsQueryBenchmarks)
            && methodName.Contains("CombinedFieldsQuery", StringComparison.Ordinal))
            flags.Add("DifferentSemantics");

        if (typeName == nameof(GeoQueryBenchmarks))
            flags.Add("DifferentIndexAlgorithm");

        if (typeName == nameof(HnswSearchBenchmarks)
            && methodName.Contains("Reference_ScalarFlatScan", StringComparison.Ordinal))
            flags.Add("ExactReference");

        if (typeName == nameof(IndexSortSearchBenchmarks)
            && methodName.Contains("LuceneNet", StringComparison.Ordinal))
            flags.Add("DifferentLifecycle");

        if (typeName == nameof(GutenbergIndexingBenchmarks)
            && parameters.TryGetValue("PersistenceProfile", out string? persistenceProfile)
            && string.Equals(persistenceProfile, "as-shipped-persistence", StringComparison.Ordinal))
            flags.Add("DirectionalPersistenceMismatch");

        return flags;
    }

    private static bool IsComparisonCaveat(string flag)
        => flag is "AdapterOverhead" or "DifferentSemantics" or "DifferentIndexAlgorithm"
            or "ExactReference" or "DifferentLifecycle" or "DirectionalPersistenceMismatch";

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
    public static BenchmarkProvenanceReport Build(
        string repoRoot,
        string gitCommitHash,
        int? effectiveDocCount,
        string artifactDirectory)
    {
        var datasets = BenchmarkDatasetSidecars.ReadAll(artifactDirectory);
        var sourceCommit = FirstNonEmpty(
            Environment.GetEnvironmentVariable("BENCH_SOURCE_COMMIT"),
            gitCommitHash);

        var hasSearchDataset = effectiveDocCount is > 0 && datasets.Any(dataset =>
            dataset.Identity.SourceKind == DataForgeSourceKind.Generated &&
            string.Equals(dataset.Identity.ProfileId, "leancorpus-search", StringComparison.Ordinal) &&
            dataset.Identity.RecordCount == effectiveDocCount.Value ||
            dataset.Identity.SourceKind == DataForgeSourceKind.Imported &&
            string.Equals(dataset.Identity.DatasetId, "leancorpus-wikipedia-en", StringComparison.Ordinal) &&
            dataset.Identity.DatasetVersion == 1 && dataset.Identity.RecordCount == 20_000);
        var corpus = hasSearchDataset
            ? BenchmarkCorpusReportBuilder.Build(effectiveDocCount)
            : null;
        return new BenchmarkProvenanceReport
        {
            SourceCommit = sourceCommit,
            SourceRef = Environment.GetEnvironmentVariable("BENCH_SOURCE_REF") ?? string.Empty,
            SourceManifestPath = Environment.GetEnvironmentVariable("BENCH_SOURCE_MANIFEST") ?? string.Empty,
            GitCommitHash = gitCommitHash,
            GitAvailable = Directory.Exists(Path.Combine(repoRoot, ".git")),
            GitDirty = TryReadGitDirty(repoRoot),
            BenchmarkDotNetVersion = GetBenchmarkDotNetVersion(),
            RscriptAvailable = IsCommandAvailable("Rscript"),
            EffectiveDocCount = effectiveDocCount,
            Datasets = datasets.Select(static evidence => evidence.Report).ToArray(),
            DataFingerprintSha256 = BuildCombinedFingerprint(datasets),
            Corpus = corpus
        };
    }

    private static string GetBenchmarkDotNetVersion()
    {
        var assembly = typeof(BenchmarkRunner).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? string.Empty;
    }

    internal static string BuildCombinedFingerprint(IReadOnlyList<BenchmarkDatasetEvidence> datasets)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var dataset in datasets.OrderBy(
                     static evidence => Convert.ToHexString(evidence.CanonicalIdentityBytes),
                     StringComparer.Ordinal))
        {
            hash.AppendData(dataset.CanonicalIdentityBytes);
            hash.AppendData([ (byte)'\n' ]);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
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

    private static bool IsCommandAvailable(string command)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            process.WaitForExit(2000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

internal static class BenchmarkCorpusReportBuilder
{
    public static BenchmarkCorpusReport? Build(int? documentCount)
    {
        if (documentCount is not > 0)
            return null;

        var documents = BenchmarkData.BuildDocuments(documentCount.Value);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var sink = new CorpusTokenSink();
        var analyser = new Rowles.LeanCorpus.Analysis.Analysers.StandardAnalyser();
        long byteCount = 0;

        foreach (var body in documents)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            byteCount += bytes.Length;
            hash.AppendData(bytes);
            hash.AppendData([ (byte)'\n' ]);
            analyser.Analyse(body.AsSpan(), sink);
        }

        return new BenchmarkCorpusReport
        {
            DocumentCount = documents.Length,
            Utf8ByteCount = byteCount,
            TokenCount = sink.TokenCount,
            UniqueTermCount = sink.UniqueTerms.Count,
            PayloadSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()
        };
    }

    private sealed class CorpusTokenSink : Rowles.LeanCorpus.Analysis.ISpanTokenSink
    {
        public long TokenCount { get; private set; }
        public HashSet<string> UniqueTerms { get; } = new(StringComparer.Ordinal);

        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset,
            string type = Rowles.LeanCorpus.Analysis.Token.DefaultType,
            int positionIncrement = 1, byte[]? payload = null)
        {
            TokenCount++;
            UniqueTerms.Add(text.ToString());
        }
    }
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
            RegisteredBenchmarkCount = report.RegisteredBenchmarkCount,
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

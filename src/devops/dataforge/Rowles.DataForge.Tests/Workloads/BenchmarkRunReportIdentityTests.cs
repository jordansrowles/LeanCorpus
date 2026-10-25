using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using Rowles.LeanCorpus.Benchmarks;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class BenchmarkRunReportIdentityTests
{
    private const string FixturePath = ".config/devops/benchmark-correctness-fixtures/bval-14-report-job-runtime-identity.json";

    [Fact]
    public void Fixture_preserves_full_identity_and_every_terminal_or_intermediate_report()
    {
        using var fixture = LoadFixture();
        JsonElement input = fixture.RootElement.GetProperty("input");
        var reports = input.GetProperty("records")
            .EnumerateArray()
            .Select(record => BuildReport(input, record))
            .ToArray();

        var preserved = BenchmarkRunReportBuilder.PreserveReportRows(reports);

        JsonElement expected = fixture.RootElement.GetProperty("expected");
        Assert.Equal(expected.GetProperty("reportedRowsPreserved").GetInt32(), preserved.Count);
        Assert.Equal(expected.GetProperty("terminalRowsPreserved").GetInt32(),
            preserved.Count(static report => report.ResultState == "TerminalResult"));
        Assert.Equal(expected.GetProperty("uniqueCaseIdentities").GetInt32(),
            preserved.Select(static report => report.CaseIdentitySha256).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(preserved.Count, preserved.Select(static report => report.ResultRowId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(preserved.Where(static report => report.ResultState == "TerminalResult"),
            static report => Assert.True(report.PreflightPassed));
        Assert.Equal(new double?[] { 5d, 6d, 7d }, preserved
            .Where(static report => report.ResultState == "TerminalResult")
            .Select(static report => report.Statistics?.MeanNanoseconds)
            .OrderBy(static mean => mean));
        Assert.Single(preserved, static report => report.ResultState == "Intermediate");
        Assert.Single(preserved, static report => report.ResultState == "Intermediate" && report.Statistics is null);
        Assert.All(preserved.Where(static report => report.ResultState != "TerminalResult"),
            static report => Assert.False(report.PreflightPassed));
    }

    [Fact]
    public void Report_identity_changes_with_job_runtime_gc_or_toolchain_settings()
    {
        using var fixture = LoadFixture();
        JsonElement input = fixture.RootElement.GetProperty("input");
        JsonElement[] records = input.GetProperty("records").EnumerateArray().ToArray();

        BenchmarkCaseIdentitySource source = BuildIdentitySource(input, records[0]);
        BenchmarkCaseIdentityReport dry = BenchmarkRunReportBuilder.CreateCaseIdentity(source);
        BenchmarkCaseIdentityReport shortRun = BuildIdentity(input, records[1]);
        BenchmarkCaseIdentityReport otherToolchain = BenchmarkRunReportBuilder.CreateCaseIdentity(source with
        {
            ToolchainType = "BenchmarkDotNet.Toolchains.InProcess.InProcessToolchain",
            ToolchainDisplayInfo = "InProcessToolchain"
        });
        BenchmarkCaseIdentityReport reorderedInputs = BenchmarkRunReportBuilder.CreateCaseIdentity(source with
        {
            Parameters = source.Parameters.Reverse().ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            EnvironmentVariables = source.EnvironmentVariables.Reverse().ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)
        });

        Assert.NotEqual(dry.IdentitySha256, shortRun.IdentitySha256);
        Assert.NotEqual(dry.IdentitySha256, otherToolchain.IdentitySha256);
        Assert.Equal(dry.IdentitySha256, reorderedInputs.IdentitySha256);
        Assert.Equal("dry-net10", dry.JobId);
        Assert.Equal(".NET", dry.RuntimeName);
        Assert.Equal("10.0.0", dry.RuntimeVersion);
        Assert.Equal("BenchmarkDotNet.Toolchains.DotNetCli.DotNetCliToolchain", dry.ToolchainType);
        Assert.False(dry.ToolchainIsInProcess);
        Assert.Equal("false", dry.Settings["gc.server"]);
        Assert.Equal("false", dry.Settings["gc.concurrent"]);
        Assert.Equal("Throughput", dry.Settings["run.strategy"]);
        Assert.Equal("3", dry.Settings["run.iterationCount"]);
        Assert.Equal("true", shortRun.Settings["gc.server"]);
        Assert.Equal(".NET", shortRun.RuntimeName);
        Assert.Equal("11.0.0", shortRun.RuntimeVersion);
    }

    [Fact]
    public void Report_row_ids_and_order_are_stable_when_summary_reports_arrive_in_a_different_order()
    {
        using var fixture = LoadFixture();
        JsonElement input = fixture.RootElement.GetProperty("input");
        var reports = input.GetProperty("records")
            .EnumerateArray()
            .Select(record => BuildReport(input, record))
            .ToArray();

        string[] forward = BenchmarkRunReportBuilder.PreserveReportRows(reports)
            .Select(static report => report.ResultRowId)
            .ToArray();
        string[] reverse = BenchmarkRunReportBuilder.PreserveReportRows(reports.Reverse())
            .Select(static report => report.ResultRowId)
            .ToArray();

        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void Result_state_uses_success_terminal_statistics_and_actual_workload_measurements()
    {
        Measurement actual = new(1, IterationMode.Workload, IterationStage.Actual, 1, 1, 10);
        Measurement warmup = new(1, IterationMode.Workload, IterationStage.Warmup, 1, 1, 1);
        var validStatistics = new BenchmarkStatisticsReport
        {
            SampleCount = 1,
            MeanNanoseconds = 10,
            MedianNanoseconds = 10,
            MinNanoseconds = 10,
            MaxNanoseconds = 10,
            StandardDeviationNanoseconds = 0,
            CoefficientOfVariationPercent = 0,
            OperationsPerSecond = 100_000_000
        };
        var invalidStatistics = new BenchmarkStatisticsReport
        {
            SampleCount = 1,
            MeanNanoseconds = double.NaN,
            MedianNanoseconds = 10,
            MinNanoseconds = 10,
            MaxNanoseconds = 10,
            StandardDeviationNanoseconds = 0
        };

        Assert.Equal("TerminalResult", BenchmarkRunReportBuilder.ClassifyResultState(true, validStatistics, [actual]));
        Assert.Equal("Intermediate", BenchmarkRunReportBuilder.ClassifyResultState(true, validStatistics, [warmup]));
        Assert.Equal("InvalidStatistics", BenchmarkRunReportBuilder.ClassifyResultState(true, null, [actual]));
        Assert.Equal("Failed", BenchmarkRunReportBuilder.ClassifyResultState(false, validStatistics, [actual]));
        Assert.False(BenchmarkRunReportBuilder.HasValidStatistics(invalidStatistics));
    }

    [Fact]
    public void Live_benchmark_case_identity_captures_the_resolved_BDN_job_runtime_and_toolchain()
    {
        Job job = Job.Dry.WithId("identity-probe").WithGcServer(true).WithIterationCount(3);
        var config = ManualConfig.CreateEmpty().AddJob(job);
        BenchmarkRunInfo runInfo = BenchmarkConverter.TypeToBenchmarks(typeof(BenchmarkIdentityProbe), config);
        BenchmarkCase benchmarkCase = Assert.Single(runInfo.BenchmarksCases);
        Job caseJob = benchmarkCase.Job ?? throw new InvalidOperationException("BenchmarkDotNet test case has no job.");

        BenchmarkCaseIdentityReport identity = BenchmarkRunReportBuilder.CreateCaseIdentity(benchmarkCase);

        Assert.True(identity.IdentityComplete);
        Assert.Equal(caseJob.Id, identity.JobId);
        Assert.Equal(caseJob.ResolvedId, identity.JobResolvedId);
        var runtime = benchmarkCase.GetRuntime();
        Assert.Equal(runtime.Name, identity.RuntimeName);
        Assert.Equal(runtime.Version?.ToString(), identity.RuntimeVersion);
        IToolchain toolchain = caseJob.Infrastructure.Toolchain
            ?? runtime.GetDefaultToolchain(benchmarkCase)
            ?? throw new InvalidOperationException("BenchmarkDotNet test case has no toolchain.");
        Assert.Equal(toolchain.GetType().FullName, identity.ToolchainType);
        Assert.Equal("true", identity.Settings["gc.server"]);
        Assert.Equal("3", identity.Settings["run.iterationCount"]);
        Assert.Equal("1000", identity.Parameters["DocumentCount"]);
    }

    private static BenchmarkCaseReport BuildReport(JsonElement input, JsonElement record)
    {
        BenchmarkCaseIdentityReport identity = BuildIdentity(input, record);
        Measurement[] measurements = record.GetProperty("measurements")
            .EnumerateArray()
            .Select(static measurement => new Measurement(
                measurement.GetProperty("launchIndex").GetInt32(),
                Enum.Parse<IterationMode>(measurement.GetProperty("iterationMode").GetString()!, ignoreCase: false),
                Enum.Parse<IterationStage>(measurement.GetProperty("iterationStage").GetString()!, ignoreCase: false),
                measurement.GetProperty("iterationIndex").GetInt32(),
                measurement.GetProperty("operations").GetInt64(),
                measurement.GetProperty("nanoseconds").GetDouble()))
            .ToArray();
        var statistics = new BenchmarkStatisticsReport
        {
            SampleCount = measurements.Length,
            MeanNanoseconds = record.GetProperty("meanNanoseconds").GetDouble(),
            MedianNanoseconds = record.GetProperty("meanNanoseconds").GetDouble(),
            MinNanoseconds = record.GetProperty("meanNanoseconds").GetDouble(),
            MaxNanoseconds = record.GetProperty("meanNanoseconds").GetDouble(),
            StandardDeviationNanoseconds = 0,
            CoefficientOfVariationPercent = 0,
            OperationsPerSecond = 1_000_000_000d / record.GetProperty("meanNanoseconds").GetDouble()
        };

        return BenchmarkRunReportBuilder.BuildCaseReport(identity, success: true, measurements, statistics, gc: null);
    }

    private static BenchmarkCaseIdentityReport BuildIdentity(JsonElement input, JsonElement record)
        => BenchmarkRunReportBuilder.CreateCaseIdentity(BuildIdentitySource(input, record));

    private static BenchmarkCaseIdentitySource BuildIdentitySource(JsonElement input, JsonElement record)
    {
        JsonElement job = record.GetProperty("job");
        JsonElement runtime = job.GetProperty("runtime");
        JsonElement toolchain = job.GetProperty("toolchain");
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gc.server"] = job.GetProperty("serverGc").GetBoolean().ToString().ToLowerInvariant(),
            ["gc.concurrent"] = job.GetProperty("concurrentGc").GetBoolean().ToString().ToLowerInvariant(),
            ["jit"] = job.GetProperty("jit").GetString()!,
            ["platform"] = job.GetProperty("platform").GetString()!,
            ["run.strategy"] = job.GetProperty("runStrategy").GetString()!,
            ["run.launchCount"] = job.GetProperty("launchCount").GetInt32().ToString(),
            ["run.warmupCount"] = job.GetProperty("warmupCount").GetInt32().ToString(),
            ["run.iterationCount"] = job.GetProperty("iterationCount").GetInt32().ToString(),
            ["run.invocationCount"] = job.GetProperty("invocationCount").GetInt64().ToString(),
            ["run.unrollFactor"] = job.GetProperty("unrollFactor").GetInt32().ToString()
        };
        var parameters = input.GetProperty("parameters").EnumerateObject()
            .ToDictionary(static property => property.Name, static property => property.Value.GetString()!, StringComparer.Ordinal);
        var environmentVariables = job.GetProperty("environmentVariables").EnumerateObject()
            .ToDictionary(static property => property.Name, static property => property.Value.GetString()!, StringComparer.Ordinal);

        var source = new BenchmarkCaseIdentitySource(
            TypeName: input.GetProperty("method").GetString()!.Split('.')[0],
            MethodName: input.GetProperty("method").GetString()!.Split('.')[1],
            DisplayInfo: job.GetProperty("displayInfo").GetString()!,
            Parameters: parameters,
            JobId: job.GetProperty("id").GetString()!,
            JobResolvedId: job.GetProperty("resolvedId").GetString()!,
            JobDisplayInfo: job.GetProperty("displayInfo").GetString()!,
            RuntimeName: runtime.GetProperty("name").GetString()!,
            RuntimeVersion: runtime.GetProperty("version").GetString()!,
            ToolchainType: toolchain.GetProperty("type").GetString()!,
            ToolchainDisplayInfo: toolchain.GetProperty("displayInfo").GetString()!,
            ToolchainRuntimeName: toolchain.GetProperty("runtimeName").GetString()!,
            ToolchainRuntimeVersion: toolchain.GetProperty("runtimeVersion").GetString()!,
            ToolchainIsInProcess: toolchain.GetProperty("inProcess").GetBoolean(),
            Settings: settings,
            EnvironmentVariables: environmentVariables);

        return source;
    }

    private static JsonDocument LoadFixture()
    {
        var path = Path.Combine(BenchmarkHelpers.RepositoryRoot, FixturePath);
        Assert.True(File.Exists(path), $"Required BVAL-14 fixture is missing: {path}");
        return JsonDocument.Parse(File.ReadAllBytes(path));
    }
}

public class BenchmarkIdentityProbe
{
    [Params(1000)]
    public int DocumentCount { get; set; }

    [Benchmark]
    public int TermQuery() => DocumentCount + 1;
}

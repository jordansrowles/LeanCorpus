using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Benchmarks;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class BenchmarkRunIdentityTests
{
    private const string SourceCommit = "940334da5e5132e4a2d8378e21e8eedc1771ca95";
    private const string Fingerprint = "a3d0bfead0e32241fcb0c7d0e954f64c72d7a1e0f08e2b18b46b9a983ef88c12";

    [Fact]
    public void ValidIdentityDoesNotByItselfQualifyAComparison()
    {
        var result = BenchmarkRunIdentityValidator.Assess(CreateIdentity());
        var final = BenchmarkQualificationFinaliser.Finalise(result,
            new BenchmarkComparisonAssessment("Unqualified", false, ["NoPairedRows"], []), "Completed");

        Assert.Equal("Validated", result.IdentityStatus);
        Assert.True(result.Validated);
        Assert.Empty(result.ReasonCodes);
        Assert.Equal("Unqualified", final.State);
        Assert.False(final.EligibleForRatio);
        Assert.False(final.Comparable);
        Assert.Contains("NoPairedRows", final.ReasonCodes);
    }

    [Fact]
    public void RejectsHistoricalSchemaFiveBenchmarkReport()
    {
        var result = BenchmarkRunIdentityValidator.Assess(CreateIdentity(
            coreReports: [CreateReport(schemaVersion: 5)]));

        Assert.Equal("Rejected", result.IdentityStatus);
        Assert.Contains("HistoricalResultSchema", result.ReasonCodes);
    }

    [Theory]
    [InlineData("0.16.0-preview.2+962da56e99ff7ab706c3c6f0aeb24e4af91a0d4f", true)]
    [InlineData("0.16.0-preview.1", false)]
    [InlineData("0.18.0", false)]
    public void IdentityUsesTheSameBenchmarkDotNetVersionPolicy(string version, bool accepted)
    {
        var result = BenchmarkRunIdentityValidator.Assess(CreateIdentity(
            coreReports: [CreateReport(benchmarkDotNetVersion: version)]));

        Assert.Equal(accepted, result.Validated);
        Assert.Equal(!accepted, result.ReasonCodes.Contains("BenchmarkDotNetVersionMismatch"));
    }

    [Fact]
    public void RejectsHistoricalSchemaFourBenchmarkReport()
    {
        var result = BenchmarkRunIdentityValidator.Assess(CreateIdentity(
            coreReports: [CreateReport(schemaVersion: 4)]));

        Assert.Equal("Rejected", result.IdentityStatus);
        Assert.False(result.Validated);
        Assert.Contains("HistoricalResultSchema", result.ReasonCodes);
    }

    [Fact]
    public void RejectsAugust2026EvenWhenReportUsesTheCurrentSchema()
    {
        var result = BenchmarkRunIdentityValidator.Assess(CreateIdentity(
            coreReports: [CreateReport(generatedAtUtc: new DateTimeOffset(2026, 8, 31, 23, 59, 0, TimeSpan.Zero))]));

        Assert.Contains("HistoricalAugust2026Result", result.ReasonCodes);
        Assert.Equal("Rejected", result.IdentityStatus);
        Assert.False(result.Validated);
    }

    [Fact]
    public void RejectsDirtyOrMismatchedSourceIdentity()
    {
        var dirty = BenchmarkRunIdentityValidator.Assess(CreateIdentity(sourceDirty: true));
        var mismatched = BenchmarkRunIdentityValidator.Assess(CreateIdentity(
            coreReports: [CreateReport(sourceCommit: new string('b', 40))]));

        Assert.Contains("SourceTreeDirty", dirty.ReasonCodes);
        Assert.Contains("ChildSourceMismatch", mismatched.ReasonCodes);
        Assert.All([dirty, mismatched], result => Assert.False(result.Validated));
    }

    [Fact]
    public void RejectsMismatchedChildHostIdentity()
    {
        var result = BenchmarkRunIdentityValidator.Assess(CreateIdentity(
            coreReports: [CreateReport(hostMachineName: "different-host")]));

        Assert.Equal("Rejected", result.IdentityStatus);
        Assert.Contains("HostMachineMismatch", result.ReasonCodes);
        Assert.False(result.Validated);
    }

    [Theory]
    [InlineData("", "DatasetFingerprintMissing")]
    [InlineData("not-a-sha256", "DatasetFingerprintInvalid")]
    public void RejectsMissingOrInvalidDatasetFingerprint(string datasetFingerprint, string reason)
    {
        var result = BenchmarkRunIdentityValidator.Assess(CreateIdentity(
            coreReports: [CreateReport(datasetFingerprintSha256: datasetFingerprint)]));

        Assert.Contains(reason, result.ReasonCodes);
        Assert.Equal("Rejected", result.IdentityStatus);
        Assert.False(result.Validated);
    }

    [Fact]
    public void RejectsMissingDatasetIdentityEvenWhenFingerprintIsWellFormed()
    {
        var result = BenchmarkRunIdentityValidator.Assess(CreateIdentity(
            coreReports: [CreateReport(hasDatasetIdentity: false)]));

        Assert.Equal("Rejected", result.IdentityStatus);
        Assert.Contains("DatasetIdentityMissing", result.ReasonCodes);
        Assert.False(result.Validated);
    }

    [Fact]
    public void RejectsMissingHostProcessorIdentity()
    {
        var result = BenchmarkRunIdentityValidator.Assess(CreateIdentity(
            host: CreateHost(cpuIdentifier: string.Empty)));

        Assert.Equal("Rejected", result.IdentityStatus);
        Assert.Contains("HostIdentityIncomplete", result.ReasonCodes);
        Assert.False(result.Validated);
    }

    [Fact]
    public void RejectsMissingCoreReportForEveryPlannedCoreInvocation()
    {
        var result = BenchmarkRunIdentityValidator.Assess(CreateIdentity(
            coreReports: [],
            expectedCanonicalReportCounts: new Dictionary<string, int>(StringComparer.Ordinal) { ["core"] = 1 }));

        Assert.Equal("Rejected", result.IdentityStatus);
        Assert.Contains("CoreReportIdentityMissing", result.ReasonCodes);
        Assert.Contains("DatasetIdentityMissing", result.ReasonCodes);
        Assert.False(result.Validated);
    }

    [Fact]
    public void TextOnlyRunWithItsCanonicalReportValidates()
    {
        var result = BenchmarkRunIdentityValidator.Assess(CreateIdentity(
            coreReports: [CreateReport() with { Project = "text" }],
            expectedCanonicalReportCounts: new Dictionary<string, int>(StringComparer.Ordinal) { ["text"] = 1 }));

        Assert.Equal("Validated", result.IdentityStatus);
        Assert.Empty(result.ReasonCodes);
    }

    [Fact]
    public void TextOnlyRunWithoutItsReportOrDatasetSidecarFailsClosed()
    {
        var expected = new Dictionary<string, int>(StringComparer.Ordinal) { ["text"] = 1 };
        var missingReport = BenchmarkRunIdentityValidator.Assess(CreateIdentity(coreReports: [], expectedCanonicalReportCounts: expected));
        var missingSidecar = BenchmarkRunIdentityValidator.Assess(CreateIdentity(
            coreReports: [CreateReport(hasDatasetIdentity: false) with { Project = "text" }],
            expectedCanonicalReportCounts: expected));
        var coreAndText = BenchmarkRunIdentityValidator.Assess(CreateIdentity(
            coreReports: [CreateReport()],
            expectedCanonicalReportCounts: new Dictionary<string, int>(StringComparer.Ordinal) { ["core"] = 1, ["text"] = 1 }));

        Assert.Contains("CanonicalReportIdentityMissing", missingReport.ReasonCodes);
        Assert.Contains("DatasetIdentityMissing", missingReport.ReasonCodes);
        Assert.Contains("DatasetIdentityMissing", missingSidecar.ReasonCodes);
        Assert.Contains("CanonicalReportIdentityMissing", coreAndText.ReasonCodes);
        Assert.All([missingReport, missingSidecar, coreAndText], static result => Assert.False(result.Validated));
    }

    [Fact]
    public void CoreReportReaderRetainsDatasetIdentityAndProvenance()
    {
        using var directory = TemporaryDirectory.Create();
        var reportPath = Path.Combine(directory.Path, "report.json");
        File.WriteAllText(reportPath, $$"""
            {
              "schemaVersion": 4,
              "generatedAtUtc": "2026-08-01T00:00:00Z",
              "hostMachineName": "test-host",
              "provenance": {
                "sourceCommit": "{{SourceCommit}}",
                "gitDirty": false,
                "osDescription": "Test OS",
                "runtimeIdentifier": "linux-x64",
                "processArchitecture": "X64",
                "runtimeFramework": ".NET 10.0",
                "benchmarkDotNetVersion": "0.16.0-preview.2",
                "dataFingerprintSha256": "{{Fingerprint}}",
                "datasets": [{
                  "sourceKind": "Generated",
                  "profileId": "test-profile",
                  "profileVersion": 1,
                  "recordCount": 10,
                  "contentSha256": "{{Fingerprint}}"
                }]
              }
            }
            """);

        var result = BenchmarkCoreReportIdentity.Read("core", reportPath);

        Assert.Null(result.ReadFailure);
        Assert.Equal(4, result.SchemaVersion);
        Assert.Equal(SourceCommit, result.SourceCommit);
        Assert.False(result.SourceDirty);
        Assert.True(result.HasDatasetIdentity);
        Assert.Single(result.Datasets);
    }

    [Fact]
    public void CoreReportReaderValidatesSchemaFiveTerminalRowsAndComparatorRejectsMissingIdentity()
    {
        using var directory = TemporaryDirectory.Create();
        var reportPath = Path.Combine(directory.Path, "report-v5.json");
        File.WriteAllText(reportPath, ValidSchemaFiveReport());

        BenchmarkCoreReportIdentity valid = BenchmarkCoreReportIdentity.Read("core", reportPath);

        Assert.Null(valid.ReadFailure);
        Assert.True(valid.ResultRowsValid);

        var invalid = BenchmarkRunIdentityValidator.Assess(CreateIdentity(
            coreReports: [CreateReport(resultRowsValid: false)]));

        Assert.Equal("Rejected", invalid.IdentityStatus);
        Assert.Contains("BenchmarkRowsInvalid", invalid.ReasonCodes);
        Assert.False(invalid.Validated);
    }

    [Theory]
    [InlineData("\"coefficientOfVariationPercent\": 10", "\"coefficientOfVariationPercent\": null")]
    [InlineData("\"operationsPerSecond\": 100000000", "\"operationsPerSecond\": 0")]
    [InlineData("\"measurementCount\": 3", "\"measurementCount\": 2")]
    public void CoreReportReaderRejectsIncompleteTerminalStatistics(string validField, string invalidField)
    {
        using var directory = TemporaryDirectory.Create();
        var reportPath = Path.Combine(directory.Path, "report-v5-invalid.json");
        string reportJson = ValidSchemaFiveReport();
        Assert.Contains(validField, reportJson, StringComparison.Ordinal);
        File.WriteAllText(reportPath, reportJson.Replace(validField, invalidField, StringComparison.Ordinal));

        BenchmarkCoreReportIdentity invalid = BenchmarkCoreReportIdentity.Read("core", reportPath);

        Assert.False(invalid.ResultRowsValid);
        Assert.NotNull(invalid.ResultRowsFailure);
    }

    private static string ValidSchemaFiveReport()
        => $$"""
            {
              "schemaVersion": 5,
              "registeredBenchmarkCount": 1,
              "totalBenchmarkCount": 1,
              "suites": [{
                "registeredBenchmarkCount": 1,
                "benchmarkCount": 1,
                "measuredBenchmarkCount": 1,
                "failedBenchmarkCount": 0,
                "intermediateBenchmarkCount": 0,
                "missingBenchmarkCount": 0,
                "benchmarks": [{
                  "key": "TermQuery|case={{Fingerprint}}",
                  "caseIdentitySha256": "{{Fingerprint}}",
                  "resultRowId": "{{Fingerprint}}",
                  "resultState": "TerminalResult",
                  "identityComplete": true,
                  "jobId": "short",
                  "resolvedJobId": "short",
                  "jobDisplayInfo": "ShortRun",
                  "runtimeName": ".NET",
                  "runtimeVersion": "10.0.0",
                  "toolchainType": "DotNetCliToolchain",
                  "toolchainDisplayInfo": "DotNetCliToolchain",
                  "toolchainRuntimeName": ".NET",
                  "toolchainRuntimeVersion": "10.0.0",
                  "measurementCount": 3,
                  "actualWorkloadMeasurementCount": 3,
                  "measurementFingerprintSha256": "{{Fingerprint}}",
                  "parameters": {},
                  "jobSettings": {},
                  "environmentVariables": {},
                  "statistics": {
                    "sampleCount": 3,
                    "meanNanoseconds": 10,
                    "medianNanoseconds": 10,
                    "minNanoseconds": 9,
                    "maxNanoseconds": 11,
                    "standardDeviationNanoseconds": 1,
                    "coefficientOfVariationPercent": 10,
                    "operationsPerSecond": 100000000
                  }
                }]
              }]
            }
            """;

    [Fact]
    public void CoreReportReaderAcceptsNullOptionalDatasetVersionForGeneratedProfile()
    {
        using var directory = TemporaryDirectory.Create();
        var reportPath = Path.Combine(directory.Path, "report.json");
        File.WriteAllText(reportPath, $$"""
            {
              "schemaVersion": 4,
              "provenance": {
                "datasets": [{
                  "sourceKind": "Generated",
                  "contentSha256": "{{Fingerprint}}",
                  "recordCount": 10,
                  "profileId": "test-profile",
                  "profileVersion": 1,
                  "datasetId": null,
                  "datasetVersion": null
                }]
              }
            }
            """);

        var result = BenchmarkCoreReportIdentity.Read("core", reportPath);

        Assert.Null(result.ReadFailure);
        Assert.True(result.HasDatasetIdentity);
        Assert.Equal(JsonValueKind.Null, result.Datasets[0].GetProperty("datasetVersion").ValueKind);
    }

    private static BenchmarkRunIdentityInput CreateIdentity(
        string? sourceCommit = null,
        bool sourceDirty = false,
        BenchmarkHostIdentity? host = null,
        IReadOnlyList<BenchmarkCoreReportIdentity>? coreReports = null,
        IReadOnlyDictionary<string, int>? expectedCanonicalReportCounts = null)
        => new(
            sourceCommit ?? SourceCommit,
            sourceDirty,
            host ?? CreateHost(),
            coreReports ?? [CreateReport()],
            expectedCanonicalReportCounts ?? new Dictionary<string, int>(StringComparer.Ordinal) { ["core"] = 1 });

    private static BenchmarkHostIdentity CreateHost(string cpuIdentifier = "Test CPU")
        => new("test-host", "Test OS", "linux-x64", "X64", 8, cpuIdentifier,
            ".NET 11.0", ServerGc: false, Fingerprint);

    private static BenchmarkCoreReportIdentity CreateReport(
        int? schemaVersion = 6,
        DateTimeOffset? generatedAtUtc = null,
        string? sourceCommit = null,
        bool? sourceDirty = false,
        string? hostMachineName = "test-host",
        string? osDescription = "Test OS",
        string? runtimeIdentifier = "linux-x64",
        string? processArchitecture = "X64",
        string? datasetFingerprintSha256 = null,
        bool hasDatasetIdentity = true,
        string? benchmarkDotNetVersion = "0.16.0-preview.2",
        bool resultRowsValid = true)
        => new(
            "core",
            "report.json",
            schemaVersion,
            generatedAtUtc ?? new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero),
            sourceCommit ?? SourceCommit,
            sourceDirty,
            hostMachineName,
            osDescription,
            runtimeIdentifier,
            processArchitecture,
            ".NET 10.0",
            datasetFingerprintSha256 ?? Fingerprint,
            hasDatasetIdentity,
            [],
            benchmarkDotNetVersion,
            null)
        {
            ResultRowsValid = resultRowsValid
        };

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = RepositoryTemporaryStorage.NewPath("devops-tests", "LeanCorpus benchmark identity ");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

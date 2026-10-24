using System.Collections.Immutable;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Configuration;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class BenchmarkComparisonAssessorTests
{
    private const string Commit = "940334da5e5132e4a2d8378e21e8eedc1771ca95";
    private const string Fingerprint = "a3d0bfead0e32241fcb0c7d0e954f64c72d7a1e0f08e2b18b46b9a983ef88c12";

    [Fact]
    public void MatchedSyntheticPairQualifiesOnlyWithCompleteParityAndPreflightEvidence()
    {
        var result = Assess(CreateEvidence());

        Assert.Equal("Qualified", result.QualificationState);
        Assert.True(result.EligibleForRatio);
        var comparison = Assert.Single(result.Comparisons);
        Assert.Equal("Qualified", comparison.QualificationState);
        Assert.True(comparison.EligibleForRatio);
        Assert.Equal(0.8d, comparison.MeanRatio!.Value, precision: 6);
    }

    [Fact]
    public void RejectsMismatchedDatasetEvenWhenSemanticParametersMatch()
    {
        BenchmarkComparisonEvidence evidence = CreateEvidence() with
        {
            Reports = [
                CreateReport(rows: [CreateRow("LeanCorpus_Search", 100)]) with
                { DatasetFingerprintSha256 = Fingerprint },
                CreateReport(rows: [CreateRow("LuceneNet_Search", 125)]) with
                { DatasetFingerprintSha256 = new string('b', 64) }
            ]
        };

        var result = BenchmarkComparisonAssessor.Assess(evidence);

        Assert.False(result.EligibleForRatio);
        Assert.Contains("DatasetMismatch", Assert.Single(result.Comparisons).ReasonCodes);
    }

    [Fact]
    public void RejectsDuplicateJobRowsInsteadOfSelectingTheFastestMean()
    {
        BenchmarkComparisonEvidence evidence = CreateEvidence() with
        {
            Reports = [CreateReport(rows: [
                CreateRow("LeanCorpus_Search", 100),
                CreateRow("LeanCorpus_Search", 1),
                CreateRow("LuceneNet_Search", 125)])]
        };

        var result = BenchmarkComparisonAssessor.Assess(evidence);

        Assert.False(result.EligibleForRatio);
        Assert.Contains("DuplicateCaseRows", Assert.Single(result.Comparisons).ReasonCodes);
        Assert.Null(Assert.Single(result.Comparisons).MeanRatio);
    }

    [Fact]
    public void MissingShardPartnerCannotProduceAQualifiedRatio()
    {
        var result = BenchmarkComparisonAssessor.Assess(CreateEvidence() with
        {
            Reports = [CreateReport(rows: [CreateRow("LeanCorpus_Search", 100)])]
        });

        Assert.False(result.EligibleForRatio);
        Assert.Contains("MissingCaseRows", Assert.Single(result.Comparisons).ReasonCodes);
        Assert.Null(Assert.Single(result.Comparisons).MeanRatio);
    }

    [Theory]
    [InlineData("Failed", true, 30, 4, "NonTerminalOrFailedResult")]
    [InlineData("Intermediate", true, 30, 4, "NonTerminalOrFailedResult")]
    [InlineData("TerminalResult", false, 30, 4, "IncompleteCaseIdentity")]
    [InlineData("TerminalResult", true, 10, 4, "InsufficientSamples")]
    [InlineData("TerminalResult", true, 30, 8, "NoisyMeasurements")]
    public void RejectsFailedPartialIncompleteOrLowQualityRows(
        string resultState,
        bool identityComplete,
        int sampleCount,
        double coefficientOfVariation,
        string reason)
    {
        BenchmarkComparisonEvidence evidence = CreateEvidence() with
        {
            Reports = [CreateReport(rows: [
                CreateRow("LeanCorpus_Search", 100) with
                {
                    ResultState = resultState,
                    IdentityComplete = identityComplete,
                    SampleCount = sampleCount,
                    CoefficientOfVariationPercent = coefficientOfVariation
                },
                CreateRow("LuceneNet_Search", 125)])]
        };

        var result = BenchmarkComparisonAssessor.Assess(evidence);

        Assert.False(result.EligibleForRatio);
        Assert.Contains(reason, Assert.Single(result.Comparisons).ReasonCodes);
    }

    [Theory]
    [InlineData("preflight", "PreflightFailed")]
    [InlineData("recall", "CaseQualityFlagsPresent")]
    [InlineData("cache", "CaseQualityFlagsPresent")]
    public void FailedVectorRecallAndCachedNoHitShortcutCannotQualify(string failure, string reason)
    {
        BenchmarkComparisonCase row = failure switch
        {
            "preflight" => CreateRow("LeanCorpus_Search", 100) with { PreflightPassed = false },
            "recall" => CreateRow("LeanCorpus_Search", 100) with { QualityFlags = ["RecallBelowFloor"] },
            _ => CreateRow("LeanCorpus_Search", 100) with { QualityFlags = ["CacheNoHitShortcut"] }
        };
        BenchmarkComparisonEvidence evidence = CreateEvidence() with
        {
            Reports = [CreateReport(rows: [row, CreateRow("LuceneNet_Search", 125)])]
        };

        var result = BenchmarkComparisonAssessor.Assess(evidence);

        Assert.False(result.EligibleForRatio);
        Assert.Contains(reason, Assert.Single(result.Comparisons).ReasonCodes);
    }

    [Fact]
    public void RejectsStaleV4AndDirtySource()
    {
        var historical = BenchmarkComparisonAssessor.Assess(CreateEvidence() with
        {
            Reports = [CreateReport(schemaVersion: 4, rows: [CreateRow("LeanCorpus_Search", 100), CreateRow("LuceneNet_Search", 125)])]
        });
        var dirty = BenchmarkComparisonAssessor.Assess(CreateEvidence() with { SourceDirty = true });

        Assert.False(historical.EligibleForRatio);
        Assert.Contains("HistoricalResultSchema", Assert.Single(historical.Comparisons).ReasonCodes);
        Assert.False(dirty.EligibleForRatio);
        Assert.Contains("SourceTreeDirty", Assert.Single(dirty.Comparisons).ReasonCodes);
    }

    [Theory]
    [InlineData("dataset")]
    [InlineData("host")]
    [InlineData("runtime")]
    [InlineData("gc")]
    [InlineData("pgo")]
    public void RejectsMismatchedDatasetHostRuntimeGcOrPgo(string identityKind)
    {
        BenchmarkComparisonCase right = CreateRow("LuceneNet_Search", 125);
        BenchmarkComparisonSourceReport report = CreateReport(rows: [CreateRow("LeanCorpus_Search", 100), right]);
        BenchmarkComparisonEvidence evidence = CreateEvidence();
        IReadOnlyList<BenchmarkComparisonSourceReport> reports = [report];
        switch (identityKind)
        {
            case "dataset":
                reports = [
                    CreateReport(rows: [CreateRow("LeanCorpus_Search", 100)]) with { DatasetFingerprintSha256 = Fingerprint },
                    CreateReport(rows: [CreateRow("LuceneNet_Search", 125)]) with { DatasetFingerprintSha256 = new string('b', 64) }
                ];
                break;
            case "host":
                report = report with { OsDescription = "Different OS" };
                break;
            case "runtime":
                right = right with { RuntimeVersion = "11.0.0" };
                report = report with { Rows = [CreateRow("LeanCorpus_Search", 100), right] };
                break;
            case "gc":
                right = right with { JobSettings = new Dictionary<string, string>(StringComparer.Ordinal)
                    { ["gc.server"] = "true", ["gc.concurrent"] = "true" } };
                report = report with { Rows = [CreateRow("LeanCorpus_Search", 100), right] };
                break;
            case "pgo":
                right = right with { EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                    { ["DOTNET_TieredPGO"] = "0" } };
                report = report with { Rows = [CreateRow("LeanCorpus_Search", 100), right] };
                break;
        }
        if (identityKind != "dataset")
            reports = [report];
        evidence = evidence with { Reports = reports };

        var result = BenchmarkComparisonAssessor.Assess(evidence);

        Assert.False(result.EligibleForRatio);
        Assert.NotEmpty(Assert.Single(result.Comparisons).ReasonCodes);
    }

    [Fact]
    public void NoRowsAfterAFilterProducesAnExplicitUnqualifiedAssessment()
    {
        var result = BenchmarkComparisonAssessor.Assess(CreateEvidence() with
        {
            Reports = [CreateReport(rows: [])]
        });

        Assert.Equal("Unqualified", result.QualificationState);
        Assert.False(result.EligibleForRatio);
        Assert.Contains("NoPairedRows", result.ReasonCodes);
    }

    [Fact]
    public void InvalidChildJsonIsRetainedAsAReadFailure()
    {
        string path = Path.Combine(Path.GetTempPath(), "benchmark-comparison-invalid-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{");

            BenchmarkComparisonSourceReport report = BenchmarkComparisonSourceReport.Read(path);

            Assert.Null(report.SchemaVersion);
            Assert.NotNull(report.ReadFailure);
            Assert.Empty(report.Rows);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, null)]
    public void V5ReportReaderPreservesMissingAndTerminalPreflightEvidence(bool? preflightPassed, bool? expected)
    {
        string path = Path.Combine(Path.GetTempPath(), "benchmark-comparison-preflight-" + Guid.NewGuid().ToString("N") + ".json");
        var row = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["typeName"] = "PairBenchmarks",
            ["methodName"] = "LeanCorpus_Search",
            ["parameters"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Count"] = "100" },
            ["resultState"] = "TerminalResult",
            ["identityComplete"] = true,
            ["qualityFlags"] = Array.Empty<string>()
        };
        if (preflightPassed.HasValue)
            row["preflightPassed"] = preflightPassed.Value;

        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                schemaVersion = 5,
                suites = new[] { new { benchmarks = new[] { row } } }
            }));

            BenchmarkComparisonSourceReport report = BenchmarkComparisonSourceReport.Read(path);

            Assert.Null(report.ReadFailure);
            Assert.Equal(expected, Assert.Single(report.Rows).PreflightPassed);

            BenchmarkComparisonAssessment assessment = Assess(CreateEvidence() with
            {
                Reports = [report, CreateReport(rows: [CreateRow("LuceneNet_Search", 125)])]
            });
            Assert.Equal("Unqualified", assessment.QualificationState);
            Assert.False(assessment.EligibleForRatio);
            Assert.Contains("SourceCommitMismatch", assessment.ReasonCodes);
            Assert.Contains("DatasetIdentityMissing", assessment.ReasonCodes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static BenchmarkComparisonAssessment Assess(BenchmarkComparisonEvidence evidence)
        => BenchmarkComparisonAssessor.Assess(evidence);

    private static BenchmarkComparisonEvidence CreateEvidence()
        => new(Commit, false, CreateHost(), [LeftFamily(), RightFamily()],
            [CreateReport(rows: [CreateRow("LeanCorpus_Search", 100), CreateRow("LuceneNet_Search", 125)])]);

    private static BenchmarkCaseConfiguration LeftFamily()
        => Family("left-search", "leancorpus", "LeanCorpus_Search");

    private static BenchmarkCaseConfiguration RightFamily()
        => Family("right-search", "lucenenet", "LuceneNet_Search");

    private static BenchmarkCaseConfiguration Family(string id, string engine, string method)
        => new(id, "core", "PairBenchmarks", [method], [],
            ImmutableDictionary<string, ImmutableArray<string>>.Empty.WithComparers(StringComparer.Ordinal),
            ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal),
            engine, "Search", "TermSearch", "test-profile-v1", "term-search-v1", "TermSearch",
            "Disabled", "existing-index", "term-search-pair", "QualifiedParity", "matching-document-ids",
            "BVAL-99 paired exact result assertion", [], "short");

    private static BenchmarkComparisonSourceReport CreateReport(
        int schemaVersion = 5,
        IReadOnlyList<BenchmarkComparisonCase>? rows = null)
        => new(schemaVersion, Commit, false, Fingerprint, ["test-profile-v1"],
            "test-host", "Test OS", "linux-x64", "X64", ".NET 10.0",
            "0.16.0-preview.2", rows ?? []);

    private static BenchmarkComparisonCase CreateRow(string method, double meanNanoseconds)
        => new("Tests.PairBenchmarks", method, new Dictionary<string, string>(StringComparer.Ordinal) { ["Count"] = "100" },
            "TerminalResult", true, ".NET", "10.0.0", "DotNetCliToolchain", ".NET", "10.0.0",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["gc.server"] = "false", ["gc.concurrent"] = "true" },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["DOTNET_TieredPGO"] = "1" },
            30, 30, 4, meanNanoseconds, [], true);

    private static BenchmarkHostIdentity CreateHost()
        => new("test-host", "Test OS", "linux-x64", "X64", 8, "Test CPU", ".NET 10.0", false, Fingerprint);
}

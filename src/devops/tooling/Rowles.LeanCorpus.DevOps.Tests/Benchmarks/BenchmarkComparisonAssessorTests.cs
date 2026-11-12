using System.Collections.Immutable;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Configuration;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class BenchmarkComparisonAssessorTests
{
    private const string Commit = "940334da5e5132e4a2d8378e21e8eedc1771ca95";
    private const string Fingerprint = "a3d0bfead0e32241fcb0c7d0e954f64c72d7a1e0f08e2b18b46b9a983ef88c12";
    private const string DatasetContent = "c3d0bfead0e32241fcb0c7d0e954f64c72d7a1e0f08e2b18b46b9a983ef88c13";
    private const string ProofDigest = "d3d0bfead0e32241fcb0c7d0e954f64c72d7a1e0f08e2b18b46b9a983ef88c14";

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
    [InlineData("preflight", "SemanticProofFailed")]
    [InlineData("recall", "CaseQualityFlagsPresent")]
    [InlineData("cache", "CaseQualityFlagsPresent")]
    public void FailedVectorRecallAndCachedNoHitShortcutCannotQualify(string failure, string reason)
    {
        BenchmarkComparisonCase row = failure switch
        {
            "preflight" => CreateRow("LeanCorpus_Search", 100) with { SemanticProof = Proof("LeanCorpus_Search") with { State = "Failed", Reasons = ["EngineOutputsDiffer"] } },
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
        string path = Path.Combine(RepositoryTemporaryStorage.GetAreaRoot("devops-tests"), "benchmark-comparison-invalid-" + Guid.NewGuid().ToString("N") + ".json");
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
    public void ReportReaderPreservesMethodExecutionEvidenceAndNeverTreatsItAsProof(bool? methodExecutionSucceeded, bool? expected)
    {
        string path = Path.Combine(RepositoryTemporaryStorage.GetAreaRoot("devops-tests"), "benchmark-comparison-preflight-" + Guid.NewGuid().ToString("N") + ".json");
        var row = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["typeName"] = "PairBenchmarks",
            ["methodName"] = "LeanCorpus_Search",
            ["parameters"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Count"] = "100" },
            ["resultState"] = "TerminalResult",
            ["identityComplete"] = true,
            ["qualityFlags"] = Array.Empty<string>()
        };
        if (methodExecutionSucceeded.HasValue)
            row["methodExecutionSucceeded"] = methodExecutionSucceeded.Value;

        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                schemaVersion = 6,
                suites = new[] { new { benchmarks = new[] { row } } }
            }));

            BenchmarkComparisonSourceReport report = BenchmarkComparisonSourceReport.Read(path);

            Assert.Null(report.ReadFailure);
            Assert.Equal(expected, Assert.Single(report.Rows).MethodExecutionSucceeded);
            Assert.Null(Assert.Single(report.Rows).SemanticProof);

            BenchmarkComparisonAssessment assessment = Assess(CreateEvidence() with
            {
                Reports = [report, CreateReport(rows: [CreateRow("LuceneNet_Search", 125)])]
            });
            Assert.Equal("Unqualified", assessment.QualificationState);
            Assert.False(assessment.EligibleForRatio);
            Assert.Contains("SourceCommitMismatch", assessment.ReasonCodes);
            Assert.Contains("DatasetIdentityMissing", assessment.ReasonCodes);
            Assert.Contains("SemanticProofMissing", assessment.ReasonCodes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("0.16.0-preview.2", true)]
    [InlineData("0.16.0-preview.2+962da56e99ff7ab706c3c6f0aeb24e4af91a0d4f", true)]
    [InlineData("0.16.0-preview.1", false)]
    [InlineData("0.16.0-preview.1+962da56e99ff7ab706c3c6f0aeb24e4af91a0d4f", false)]
    [InlineData("0.16.0", false)]
    [InlineData("0.17.0-preview.1", false)]
    [InlineData("0.16.0-preview.2+", false)]
    public void OneVersionPolicyAcceptsOnlyTheRequiredBenchmarkDotNetVersion(string version, bool accepted)
    {
        Assert.Equal(accepted, BenchmarkToolchainPolicy.IsAcceptedBenchmarkDotNetVersion(version));

        var result = Assess(CreateEvidence() with
        {
            Reports = [CreateReport(rows: [CreateRow("LeanCorpus_Search", 100), CreateRow("LuceneNet_Search", 125)]) with
            { BenchmarkDotNetVersion = version }]
        });

        Assert.Equal(accepted, result.EligibleForRatio);
        if (!accepted)
            Assert.Contains("BenchmarkDotNetVersionMismatch", Assert.Single(result.Comparisons).ReasonCodes);
    }

    [Fact]
    public void NetTenHostWithTwoNetElevenChildrenPassesHostIdentity()
    {
        var result = Assess(CreateEvidence() with
        {
            Host = CreateHost() with { RuntimeFramework = ".NET 10.0.7" },
            Reports = [CreateReport(rows: [NetEleven(CreateRow("LeanCorpus_Search", 100)), NetEleven(CreateRow("LuceneNet_Search", 125))]) with
            { RuntimeFramework = ".NET 11.0.0" }]
        });

        Assert.True(result.EligibleForRatio);
        Assert.Equal("Qualified", Assert.Single(result.Comparisons).QualificationState);
    }

    [Fact]
    public void NetTenChildAgainstNetElevenChildFailsWithRuntimeMismatch()
    {
        var result = Assess(CreateEvidence() with
        {
            Reports = [
                CreateReport(rows: [CreateRow("LeanCorpus_Search", 100)]) with { RuntimeFramework = ".NET 10.0.7" },
                CreateReport(rows: [NetEleven(CreateRow("LuceneNet_Search", 125))]) with { RuntimeFramework = ".NET 11.0.0" }]
        });

        Assert.False(result.EligibleForRatio);
        var pair = Assert.Single(result.Comparisons);
        Assert.Contains("RuntimeMismatch", pair.ReasonCodes);
        Assert.DoesNotContain("HostIdentityMismatch", pair.ReasonCodes);
    }

    [Fact]
    public void DifferentHostArchitectureFailsHostIdentity()
    {
        var result = Assess(CreateEvidence() with
        {
            Reports = [CreateReport(rows: [CreateRow("LeanCorpus_Search", 100), CreateRow("LuceneNet_Search", 125)]) with
            { ProcessArchitecture = "Arm64" }]
        });

        Assert.Contains("HostIdentityMismatch", Assert.Single(result.Comparisons).ReasonCodes);
    }

    [Fact]
    public void DifferentJobSettingsBetweenChildrenFailWithJobMismatch()
    {
        var right = CreateRow("LuceneNet_Search", 125);
        right = right with { JobSettings = new Dictionary<string, string>(right.JobSettings, StringComparer.Ordinal) { ["run.iterationCount"] = "30" } };

        var result = Assess(CreateEvidence() with { Reports = [CreateReport(rows: [CreateRow("LeanCorpus_Search", 100), right])] });

        Assert.Contains("JobMismatch", Assert.Single(result.Comparisons).ReasonCodes);
    }

    private static BenchmarkComparisonCase NetEleven(BenchmarkComparisonCase row)
        => row with { RuntimeVersion = "11.0.0", ToolchainRuntimeVersion = "11.0.0" };

    [Theory]
    [InlineData("wrong-hits", "SemanticProofFailed")]
    [InlineData("missing", "SemanticProofMissing")]
    [InlineData("stale", "SemanticProofStale")]
    [InlineData("dataset", "SemanticProofDatasetMismatch")]
    [InlineData("case", "SemanticProofCaseMismatch")]
    [InlineData("version", "SemanticProofVersionUnsupported")]
    public void RowWithoutAValidBoundProofStaysUnqualified(string defect, string reason)
    {
        BenchmarkSemanticProof proof = Proof("LeanCorpus_Search");
        BenchmarkComparisonCase row = CreateRow("LeanCorpus_Search", 100) with
        {
            SemanticProof = defect switch
            {
                // A method that returned a wrong hit set still finished successfully.
                "wrong-hits" => proof with { State = "Failed", Reasons = ["EngineOutputsDiffer"] },
                "missing" => null,
                "stale" => proof with { SourceCommit = new string('b', 40) },
                "dataset" => proof with { DatasetContentSha256 = new string('e', 64) },
                "case" => proof with { Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["Count"] = "200" } },
                _ => proof with { ProofVersion = 2 }
            }
        };

        var result = Assess(CreateEvidence() with { Reports = [CreateReport(rows: [row, CreateRow("LuceneNet_Search", 125)])] });

        Assert.False(result.EligibleForRatio);
        var pair = Assert.Single(result.Comparisons);
        Assert.Equal("Unqualified", pair.QualificationState);
        Assert.Contains(reason, pair.ReasonCodes);
    }

    [Fact]
    public void PassingProofWithAFailedTimedRowStaysUnqualified()
    {
        BenchmarkComparisonCase row = CreateRow("LeanCorpus_Search", 100) with
        {
            ResultState = "Failed",
            MethodExecutionSucceeded = false
        };

        var result = Assess(CreateEvidence() with { Reports = [CreateReport(rows: [row, CreateRow("LuceneNet_Search", 125)])] });

        var pair = Assert.Single(result.Comparisons);
        Assert.Equal("Unqualified", pair.QualificationState);
        Assert.Contains("MethodExecutionFailed", pair.ReasonCodes);
        Assert.Contains("NonTerminalOrFailedResult", pair.ReasonCodes);
    }

    [Fact]
    public void SchemaFiveReportsStayReadableAndUnqualified()
    {
        var result = Assess(CreateEvidence() with
        {
            Reports = [CreateReport(schemaVersion: 5, rows: [CreateRow("LeanCorpus_Search", 100), CreateRow("LuceneNet_Search", 125)])]
        });

        Assert.False(result.EligibleForRatio);
        Assert.Contains("HistoricalResultSchema", Assert.Single(result.Comparisons).ReasonCodes);
    }

    [Fact]
    public void TwentyThousandFamilyInAHundredThousandRunExplainsTheDocumentCountMismatch()
    {
        var parameters = ImmutableDictionary<string, ImmutableArray<string>>.Empty.WithComparers(StringComparer.Ordinal)
            .Add("Count", ["20000"]);
        BenchmarkComparisonEvidence evidence = CreateEvidence() with
        {
            CaseFamilies = [LeftFamily() with { ParameterValues = parameters }, RightFamily() with { ParameterValues = parameters }]
        };

        var result = Assess(evidence);

        Assert.Empty(result.Comparisons);
        Assert.Contains("NoPairedRows", result.ReasonCodes);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("NoEligibleVariantForParameters", diagnostic.Code);
        Assert.Equal(["left-search", "right-search"], diagnostic.CandidateFamilies);
        Assert.Contains("left-search: Count=100 (reviewed: 20000)", diagnostic.ParameterMismatches);

        var documentCountFamilies = ImmutableDictionary<string, ImmutableArray<string>>.Empty.WithComparers(StringComparer.Ordinal)
            .Add("DocumentCount", ["20000"]);
        BenchmarkComparisonCase Row(string method) => CreateRow(method, 100) with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["DocumentCount"] = "100000" }
        };
        var documentCount = Assess(CreateEvidence() with
        {
            CaseFamilies = [LeftFamily() with { ParameterValues = documentCountFamilies }, RightFamily() with { ParameterValues = documentCountFamilies }],
            Reports = [CreateReport(rows: [Row("LeanCorpus_Search"), Row("LuceneNet_Search")])]
        });
        Assert.Contains("NoEligibleVariantForDocumentCount", documentCount.ReasonCodes);
        Assert.Equal("NoEligibleVariantForDocumentCount", Assert.Single(documentCount.Diagnostics).Code);
    }

    [Fact]
    public void GroupThatDidNotRunAddsNoDiagnostic()
    {
        var result = Assess(CreateEvidence() with { Reports = [CreateReport(rows: [])] });

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void StatisticalFlagsAreDiagnosticsNotQualificationReasons()
    {
        BenchmarkComparisonCase row = CreateRow("LeanCorpus_Search", 100) with { QualityFlags = ["Directional"] };

        var result = Assess(CreateEvidence() with { Reports = [CreateReport(rows: [row, CreateRow("LuceneNet_Search", 125)])] });

        var pair = Assert.Single(result.Comparisons);
        Assert.Equal("Qualified", pair.QualificationState);
        Assert.DoesNotContain("CaseQualityFlagsPresent", pair.ReasonCodes);
        Assert.Equal(["Directional"], pair.DiagnosticFlags);
    }

    [Fact]
    public void EachCaseShowsItsSamplesVariabilityAndFailingGates()
    {
        BenchmarkComparisonCase row = CreateRow("LeanCorpus_Search", 100) with { SampleCount = 12, ActualWorkloadMeasurementCount = 12 };

        var result = Assess(CreateEvidence() with { Reports = [CreateReport(rows: [row, CreateRow("LuceneNet_Search", 125)])] });

        var pair = Assert.Single(result.Comparisons);
        Assert.Equal("Directional", pair.QualificationState);
        var lean = Assert.Single(pair.Cases, static item => item.EngineId == "leancorpus");
        Assert.Equal(12, lean.SampleCount);
        Assert.Equal(4d, lean.CoefficientOfVariationPercent);
        Assert.Equal(["InsufficientSamples"], lean.FailingGates);
        Assert.Empty(Assert.Single(pair.Cases, static item => item.EngineId == "lucenenet").FailingGates);
    }

    [Theory]
    [InlineData("instruction-sets", "IntrinsicsMismatch")]
    [InlineData("vector-width", "IntrinsicsMismatch")]
    [InlineData("missing", "EffectiveRuntimeMissing")]
    public void PairAcrossIntrinsicSetsFailsWithANamedReason(string difference, string reason)
    {
        BenchmarkComparisonCase right = CreateRow("LuceneNet_Search", 125) with
        {
            EffectiveRuntime = difference switch
            {
                "instruction-sets" => Runtime(["Sse", "Sse2", "Sse42", "Avx", "Avx2"]),
                "vector-width" => Runtime(vectorWidthBits: 256),
                _ => null
            }
        };

        var result = Assess(CreateEvidence() with { Reports = [CreateReport(rows: [CreateRow("LeanCorpus_Search", 100), right])] });

        Assert.False(result.EligibleForRatio);
        Assert.Contains(reason, Assert.Single(result.Comparisons).ReasonCodes);
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
        int schemaVersion = 6,
        IReadOnlyList<BenchmarkComparisonCase>? rows = null)
        => new(schemaVersion, Commit, false, Fingerprint, ["test-profile-v1"],
            "test-host", "Test OS", "linux-x64", "X64", ".NET 10.0",
            "0.16.0-preview.2", rows ?? [])
        {
            DatasetContentHashes = [DatasetContent]
        };

    private static BenchmarkComparisonCase CreateRow(string method, double meanNanoseconds)
        => new("Tests.PairBenchmarks", method, new Dictionary<string, string>(StringComparer.Ordinal) { ["Count"] = "100" },
            "TerminalResult", true, ".NET", "10.0.0", "DotNetCliToolchain", ".NET", "10.0.0",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["gc.server"] = "false", ["gc.concurrent"] = "true" },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["DOTNET_TieredPGO"] = "1" },
            30, 30, 4, meanNanoseconds, [], true, Proof(method))
        {
            EffectiveRuntime = Runtime()
        };

    private static BenchmarkEffectiveRuntime Runtime(string[]? instructionSets = null, int vectorWidthBits = 128)
        => new(".NET 10.0", false, true, "1", vectorWidthBits, instructionSets ?? ["Sse", "Sse2", "Sse42"],
            instructionSets is null && vectorWidthBits == 128 ? new string('f', 64) : new string('e', 64));

    private static BenchmarkSemanticProof Proof(string method)
        => new(1, "bval-99-pair", "bval-99-fixture", "PairBenchmarks", [method],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Count"] = "100" },
            DatasetContent, Commit, "Passed", [], ProofDigest, 1);

    private static BenchmarkHostIdentity CreateHost()
        => new("test-host", "Test OS", "linux-x64", "X64", 8, "Test CPU", ".NET 10.0", false, Fingerprint);
}

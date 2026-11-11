using Rowles.LeanCorpus.Benchmarks;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class BenchmarkSemanticProofTests
{
    private static readonly Dictionary<string, string> Parameters = new(StringComparer.Ordinal) { ["DocumentCount"] = "20000" };

    [Fact]
    public void Matching_passed_proofs_combine_into_a_passed_row_proof()
    {
        var proof = BenchmarkSemanticProofs.ForRow([Proof(), Proof()], "Rowles.LeanCorpus.Benchmarks.PairBenchmarks", "LeanCorpus_Search", Parameters);

        Assert.NotNull(proof);
        Assert.Equal(BenchmarkSemanticProofs.Passed, proof.State);
        Assert.Equal(2, proof.VerificationCount);
        Assert.Empty(proof.Reasons);
    }

    [Fact]
    public void Row_without_a_matching_proof_has_none()
    {
        Assert.Null(BenchmarkSemanticProofs.ForRow([Proof()], "PairBenchmarks", "LuceneNet_Other", Parameters));
        Assert.Null(BenchmarkSemanticProofs.ForRow([Proof()], "PairBenchmarks", "LeanCorpus_Search",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["DocumentCount"] = "100000" }));
    }

    [Theory]
    [InlineData("failed", "EngineOutputsDiffer")]
    [InlineData("digest", "ConflictingProofs")]
    [InlineData("dataset", "ConflictingProofs")]
    [InlineData("unverified", "ProofHasNoVerification")]
    public void Any_failed_conflicting_or_unverified_proof_fails_the_row(string defect, string reason)
    {
        BenchmarkSemanticProofReport other = Proof();
        switch (defect)
        {
            case "failed":
                other.State = BenchmarkSemanticProofs.Failed;
                other.Reasons = ["EngineOutputsDiffer"];
                break;
            case "digest":
                other.DigestSha256 = new string('e', 64);
                break;
            case "dataset":
                other.DatasetContentSha256 = new string('f', 64);
                break;
            default:
                other.VerificationCount = 0;
                break;
        }
        BenchmarkSemanticProofReport[] proofs = defect == "unverified" ? [other] : [Proof(), other];

        var proof = BenchmarkSemanticProofs.ForRow(proofs, "PairBenchmarks", "LeanCorpus_Search", Parameters);

        Assert.NotNull(proof);
        Assert.Equal(BenchmarkSemanticProofs.Failed, proof.State);
        Assert.Contains(reason, proof.Reasons);
    }

    private static BenchmarkSemanticProofReport Proof() => new()
    {
        ProofId = "pair",
        FixtureId = "fixture",
        TypeName = "PairBenchmarks",
        Methods = ["LeanCorpus_Search", "LuceneNet_Search"],
        Parameters = new Dictionary<string, string>(Parameters, StringComparer.Ordinal),
        DatasetContentSha256 = new string('c', 64),
        SourceCommit = new string('a', 40),
        State = BenchmarkSemanticProofs.Passed,
        DigestSha256 = new string('d', 64),
        VerificationCount = 1
    };
}

public sealed class BenchmarkWarningCodeTests
{
    [Theory]
    [InlineData("MinIterationTime", "The minimum observed iteration time is 1.0ms which is very small.", "MinIterationTime")]
    [InlineData("MultimodalDistribution", "It seems that the distribution is bimodal (mValue = 3.4)", "BimodalDistribution")]
    [InlineData("MultimodalDistribution", "It seems that the distribution is multimodal (mValue = 4.1)", "MultimodalDistribution")]
    [InlineData("MultimodalDistribution", "It seems that the distribution can have several modes (mValue = 2.9)", "PossibleMultimodalDistribution")]
    public void Analyser_conclusions_map_to_stable_warning_codes(string analyserId, string message, string expected)
        => Assert.Equal(expected, BenchmarkRunReportBuilder.WarningCode(analyserId, message));
}

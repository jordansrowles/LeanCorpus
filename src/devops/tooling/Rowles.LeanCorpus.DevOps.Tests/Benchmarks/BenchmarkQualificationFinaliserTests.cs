using Rowles.LeanCorpus.DevOps.Benchmarks;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class BenchmarkQualificationFinaliserTests
{
    private static readonly BenchmarkRunIdentityAssessment Validated = new("Validated", []);
    private static readonly BenchmarkRunIdentityAssessment Rejected = new("Rejected", ["SourceTreeDirty"]);

    [Fact]
    public void ValidSmallTwoEnginePairQualifiesTheRunForThatPair()
    {
        var final = BenchmarkQualificationFinaliser.Finalise(Validated, Comparison(QualifiedPair()), "Completed");

        Assert.Equal("Qualified", final.State);
        Assert.True(final.EligibleForRatio);
        Assert.True(final.Comparable);
        Assert.Equal(BenchmarkQualificationFinaliser.ScopeAllPairs, final.Scope);
        var pair = Assert.Single(final.Pairs);
        Assert.Equal("Qualified", pair.State);
        Assert.True(pair.EligibleForRatio);
        Assert.Equal(0.8d, pair.MeanRatio);
    }

    [Fact]
    public void FailedProofFailsClosed()
    {
        var final = BenchmarkQualificationFinaliser.Finalise(Validated,
            Comparison(UnqualifiedPair("SemanticProofFailed")), "Completed");

        Assert.Equal("Unqualified", final.State);
        Assert.False(final.EligibleForRatio);
        Assert.Contains("NoQualifiedPairs", final.ReasonCodes);
        var pair = Assert.Single(final.Pairs);
        Assert.Equal("Unqualified", pair.State);
        Assert.Contains("SemanticProofFailed", pair.ReasonCodes);
        Assert.Null(pair.MeanRatio);
    }

    [Fact]
    public void IncompleteMethodFailsClosed()
    {
        var final = BenchmarkQualificationFinaliser.Finalise(Validated,
            Comparison(UnqualifiedPair("MissingCaseRows")), "Completed");

        Assert.Equal("Unqualified", final.State);
        Assert.Contains("MissingCaseRows", Assert.Single(final.Pairs).ReasonCodes);
    }

    [Fact]
    public void StatisticalGatesAloneMakeAPairDirectionalNotQualified()
    {
        var pair = UnqualifiedPair("InsufficientSamples") with { QualificationState = "Directional" };

        var final = BenchmarkQualificationFinaliser.Finalise(Validated, Comparison(pair), "Completed");

        Assert.Equal("Unqualified", final.State);
        Assert.Equal(1, final.DirectionalPairCount);
        Assert.Equal("Directional", Assert.Single(final.Pairs).State);
        Assert.False(Assert.Single(final.Pairs).EligibleForRatio);
    }

    [Fact]
    public void OneQualifiedPairInAMixedRunIsScopedToThatPair()
    {
        var final = BenchmarkQualificationFinaliser.Finalise(Validated,
            Comparison(QualifiedPair(), UnqualifiedPair("SemanticProofMissing") with { ComparisonGroup = "other" }), "Completed");

        Assert.Equal("Qualified", final.State);
        Assert.Equal(BenchmarkQualificationFinaliser.ScopeQualifiedPairsOnly, final.Scope);
        Assert.Contains("SomePairsUnqualified", final.ReasonCodes);
        Assert.Single(final.Pairs, static pair => pair.EligibleForRatio);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("execution")]
    [InlineData("no-pairs")]
    [InlineData("inconsistent")]
    public void NoRouteToAQualifiedRunWithMissingEvidence(string missing)
    {
        BenchmarkRunIdentityAssessment identity = missing == "identity" ? Rejected : Validated;
        string execution = missing == "execution" ? "FailedPair" : "Completed";
        BenchmarkComparisonAssessment comparison = missing switch
        {
            "no-pairs" => new BenchmarkComparisonAssessment("Unqualified", false, ["NoPairedRows"], []),
            // A pair claiming Qualified without a ratio is evidence the finaliser must not trust.
            "inconsistent" => Comparison(QualifiedPair() with { MeanRatio = null }),
            _ => Comparison(QualifiedPair())
        };

        var final = BenchmarkQualificationFinaliser.Finalise(identity, comparison, execution);

        Assert.Equal("Unqualified", final.State);
        Assert.False(final.EligibleForRatio);
        Assert.False(final.Comparable);
        Assert.Equal(BenchmarkQualificationFinaliser.ScopeNone, final.Scope);
        Assert.All(final.Pairs, static pair => Assert.False(pair.EligibleForRatio));
        Assert.NotEmpty(final.ReasonCodes);
    }

    private static BenchmarkComparisonAssessment Comparison(params BenchmarkComparisonPairAssessment[] pairs)
        => new(pairs.All(static pair => pair.EligibleForRatio) ? "Qualified" : "Unqualified",
            pairs.All(static pair => pair.EligibleForRatio),
            pairs.SelectMany(static pair => pair.ReasonCodes).Distinct().ToArray(),
            pairs);

    private static BenchmarkComparisonPairAssessment QualifiedPair()
        => new("pair", "[]", "left", "leancorpus", "right", "lucenenet", "Qualified", true, 0.8d, []);

    private static BenchmarkComparisonPairAssessment UnqualifiedPair(string reason)
        => new("pair", "[]", "left", "leancorpus", "right", "lucenenet", "Unqualified", false, null, [reason]);
}

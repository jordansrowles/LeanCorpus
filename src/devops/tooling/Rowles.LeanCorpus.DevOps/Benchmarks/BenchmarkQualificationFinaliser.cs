namespace Rowles.LeanCorpus.DevOps.Benchmarks;

/// <summary>The finalised qualification of one reviewed pair (ADR047).</summary>
internal sealed record BenchmarkFinalPairQualification(
    string ComparisonGroup,
    string ParameterKey,
    string LeftFamilyId,
    string RightFamilyId,
    string State,
    bool EligibleForRatio,
    double? MeanRatio,
    IReadOnlyList<string> ReasonCodes);

/// <summary>
/// The finalised, fail-closed qualification of a run: derived only from run identity, execution
/// status and the per-pair semantic comparison assessment (ADR047).
/// </summary>
internal sealed record BenchmarkFinalQualification(
    string IdentityStatus,
    string State,
    bool EligibleForRatio,
    string Scope,
    int AssessedPairCount,
    int QualifiedPairCount,
    int DirectionalPairCount,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<BenchmarkFinalPairQualification> Pairs)
{
    public bool Comparable => State == "Qualified" && EligibleForRatio;
}

internal static class BenchmarkQualificationFinaliser
{
    public const string ScopeNone = "none";
    public const string ScopeAllPairs = "all-pairs";
    public const string ScopeQualifiedPairsOnly = "qualified-pairs-only";

    /// <summary>
    /// A pair is <c>Qualified</c> only with a validated identity and no reason code, and
    /// <c>Directional</c> when it fails only the statistical gates. The run is <c>Qualified</c>
    /// only when identity is validated, execution completed and at least one pair qualified;
    /// a pair's ratio is eligible only inside a qualified run.
    /// </summary>
    public static BenchmarkFinalQualification Finalise(
        BenchmarkRunIdentityAssessment identity,
        BenchmarkComparisonAssessment comparison,
        string executionClassification)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(comparison);

        var runReasons = new SortedSet<string>(StringComparer.Ordinal);
        if (!identity.Validated)
        {
            runReasons.Add("RunIdentityRejected");
            runReasons.UnionWith(identity.ReasonCodes);
        }
        if (!string.Equals(executionClassification, "Completed", StringComparison.Ordinal))
            runReasons.Add("ExecutionIncomplete");
        if (comparison.Comparisons.Count == 0)
            runReasons.UnionWith(comparison.ReasonCodes.Count == 0 ? ["NoPairedRows"] : comparison.ReasonCodes);

        var pairs = new List<(BenchmarkComparisonPairAssessment Source, string State, SortedSet<string> Reasons)>();
        foreach (var pair in comparison.Comparisons)
        {
            var reasons = new SortedSet<string>(pair.ReasonCodes, StringComparer.Ordinal);
            string state = pair.QualificationState;
            if (state == "Qualified" && (!pair.EligibleForRatio || pair.MeanRatio is null || reasons.Count != 0))
            {
                state = "Unqualified";
                reasons.Add("PairEvidenceInconsistent");
            }
            else if (state is not ("Qualified" or "Directional" or "Unqualified"))
            {
                state = "Unqualified";
                reasons.Add("PairStateUnknown");
            }
            if (!identity.Validated)
            {
                state = "Unqualified";
                reasons.Add("RunIdentityRejected");
            }
            pairs.Add((pair, state, reasons));
        }

        int qualified = pairs.Count(static pair => pair.State == "Qualified");
        int directional = pairs.Count(static pair => pair.State == "Directional");
        if (comparison.Comparisons.Count > 0 && qualified == 0)
            runReasons.Add("NoQualifiedPairs");

        bool runQualified = identity.Validated &&
            string.Equals(executionClassification, "Completed", StringComparison.Ordinal) &&
            qualified > 0;
        string scope = !runQualified ? ScopeNone : qualified == pairs.Count ? ScopeAllPairs : ScopeQualifiedPairsOnly;
        if (runQualified && qualified < pairs.Count)
            runReasons.Add("SomePairsUnqualified");

        var finalPairs = pairs.Select(pair => new BenchmarkFinalPairQualification(
                pair.Source.ComparisonGroup,
                pair.Source.ParameterKey,
                pair.Source.LeftFamilyId,
                pair.Source.RightFamilyId,
                pair.State,
                EligibleForRatio: runQualified && pair.State == "Qualified",
                runQualified && pair.State == "Qualified" ? pair.Source.MeanRatio : null,
                pair.Reasons.ToArray()))
            .ToArray();

        return new BenchmarkFinalQualification(
            identity.IdentityStatus,
            runQualified ? "Qualified" : "Unqualified",
            runQualified,
            scope,
            pairs.Count,
            qualified,
            directional,
            runReasons.ToArray(),
            finalPairs);
    }
}

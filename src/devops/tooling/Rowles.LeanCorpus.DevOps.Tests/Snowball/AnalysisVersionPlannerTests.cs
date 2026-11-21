using Rowles.LeanCorpus.DevOps.Snowball;

namespace Rowles.LeanCorpus.DevOps.Tests.Snowball;

public sealed class AnalysisVersionPlannerTests
{
    private static readonly Dictionary<string, string> Code = new(StringComparer.Ordinal) { ["german"] = "code-1" };

    private static AnalysisVersions Versions(string deVersion = "3.2.0", string? code = "code-1", string? fingerprint = "fp-1", string? fingerprintVersion = null)
        => new(1,
        [
            new AnalysisLanguage("de", AnalysisVersions.SnowballLine, deVersion, "Snowball german", "german", "3.2.0", code, fingerprint, fingerprintVersion ?? deVersion),
            new AnalysisLanguage("sk", AnalysisVersions.RowlesLine, "0.1.0", "SlovakStemmer", Fingerprint: "sk-1", FingerprintVersion: "0.1.0"),
        ]);

    private static AnalysisVersionRequests Requests(
        Dictionary<string, string>? pending = null,
        string[]? rebaseline = null,
        Dictionary<string, AnalysisBump>? bump = null,
        Dictionary<string, string>? equivalent = null)
        => new(equivalent ?? new Dictionary<string, string>(), (rebaseline ?? []).ToHashSet(StringComparer.Ordinal), bump ?? new Dictionary<string, AnalysisBump>(), pending);

    [Fact]
    public void IdenticalCode_KeepsTheIdentity()
    {
        var (versions, changes) = AnalysisVersionPlanner.Apply(Versions(), "3.3.0", Code, Requests());

        Assert.Equal("rowles-analysis/de/3.2.0", versions.Languages[0].Identity);
        Assert.Equal("3.3.0", versions.Languages[0].CompiledSnowballVersion);
        Assert.Empty(changes);
    }

    [Fact]
    public void ChangedCode_TakesTheNewSnowballVersion()
    {
        var (versions, changes) = AnalysisVersionPlanner.Apply(Versions(), "3.3.0", new Dictionary<string, string> { ["german"] = "code-2" }, Requests());

        Assert.Equal("rowles-analysis/de/3.3.0", versions.Languages[0].Identity);
        Assert.Single(changes);
    }

    [Fact]
    public void ChangedCode_OnTheSameSnowballVersion_NeedsADecision()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => AnalysisVersionPlanner.Apply(Versions(), "3.2.0", new Dictionary<string, string> { ["german"] = "code-2" }, Requests()));
        Assert.Contains("--equivalent de", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Equivalent_KeepsTheIdentityAndRecordsTheReason()
    {
        var (versions, _) = AnalysisVersionPlanner.Apply(
            Versions(), "3.3.0", new Dictionary<string, string> { ["german"] = "code-2" }, Requests(equivalent: new() { ["de"] = "refactor only" }));

        Assert.Equal("3.2.0", versions.Languages[0].Version);
        Assert.Equal("refactor only", versions.Languages[0].EquivalenceNote);
        Assert.Equal("code-2", versions.Languages[0].CodeSha256);
    }

    [Fact]
    public void FirstFingerprint_IsRecordedWithoutABump()
    {
        var (versions, _) = AnalysisVersionPlanner.Apply(
            Versions(fingerprint: null), "3.2.0", Code, Requests(pending: new() { ["de"] = "fp-new" }));

        Assert.Equal("fp-new", versions.Languages[0].Fingerprint);
        Assert.Equal("3.2.0", versions.Languages[0].Version);
    }

    [Fact]
    public void ChangedOutput_WithoutAnIdentityChange_Fails()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => AnalysisVersionPlanner.Apply(Versions(), "3.2.0", Code, Requests(pending: new() { ["de"] = "fp-2" })));
        Assert.Contains("--rebaseline de", exception.Message, StringComparison.Ordinal);

        var rowles = Assert.Throws<InvalidOperationException>(
            () => AnalysisVersionPlanner.Apply(Versions(), "3.2.0", Code, Requests(pending: new() { ["sk"] = "sk-2" })));
        Assert.Contains("--bump sk=minor", rowles.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rebaseline_AssignsTheNextRevision()
    {
        var (once, _) = AnalysisVersionPlanner.Apply(Versions(), "3.2.0", Code, Requests(pending: new() { ["de"] = "fp-2" }, rebaseline: ["de"]));
        Assert.Equal("rowles-analysis/de/3.2.0+r2", once.Languages[0].Identity);

        var (twice, _) = AnalysisVersionPlanner.Apply(once, "3.2.0", Code, Requests(pending: new() { ["de"] = "fp-3" }, rebaseline: ["de"]));
        Assert.Equal("rowles-analysis/de/3.2.0+r3", twice.Languages[0].Identity);
    }

    /// <summary>
    /// PR #131 review: a LeanCorpus-only change to the generated code at the same Snowball version,
    /// with changed output, must be recordable with --rebaseline. It takes one revision, not two.
    /// </summary>
    [Fact]
    public void Rebaseline_AcceptsChangedCodeAndOutputAtTheSameSnowballVersion()
    {
        var changed = new Dictionary<string, string>(StringComparer.Ordinal) { ["german"] = "code-2" };
        var (versions, changes) = AnalysisVersionPlanner.Apply(
            Versions(), "3.2.0", changed, Requests(pending: new() { ["de"] = "fp-2", ["sk"] = "sk-1" }, rebaseline: ["de"]));

        var de = versions.Languages[0];
        Assert.Equal("rowles-analysis/de/3.2.0+r2", de.Identity);
        Assert.Equal("code-2", de.CodeSha256);
        Assert.Equal("fp-2", de.Fingerprint);
        Assert.Equal("3.2.0+r2", de.FingerprintVersion);
        Assert.Equal("3.2.0", de.CompiledSnowballVersion);
        Assert.Contains(changes, static change => change.Contains("rebaselined", StringComparison.Ordinal));
        Assert.Equal("rowles-analysis/sk/0.1.0", versions.Languages[1].Identity);

        // The rebaselined result is stable: the same inputs again change nothing.
        var (again, noChanges) = AnalysisVersionPlanner.Apply(versions, "3.2.0", changed, Requests(pending: new() { ["de"] = "fp-2", ["sk"] = "sk-1" }));
        Assert.Equal(versions.Languages, again.Languages);
        Assert.Empty(noChanges);
    }

    [Fact]
    public void NewSnowballVersion_ResetsTheRevision()
    {
        var (versions, _) = AnalysisVersionPlanner.Apply(Versions(deVersion: "3.2.0+r2"), "3.3.0", new Dictionary<string, string> { ["german"] = "code-2" }, Requests());
        Assert.Equal("3.3.0", versions.Languages[0].Version);
    }

    [Theory]
    [InlineData("Minor", "0.2.0")]
    [InlineData("Patch", "0.1.1")]
    public void Bump_AppliesToRowlesLanguages(string kind, string expected)
    {
        var bump = Enum.Parse<AnalysisBump>(kind);
        var (versions, _) = AnalysisVersionPlanner.Apply(
            Versions(), "3.2.0", Code, Requests(pending: new() { ["sk"] = "sk-2" }, bump: new() { ["sk"] = bump }));
        Assert.Equal($"rowles-analysis/sk/{expected}", versions.Languages[1].Identity);
    }

    [Fact]
    public void BumpWithoutAnOutputChange_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => AnalysisVersionPlanner.Apply(
            Versions(), "3.2.0", Code, Requests(pending: new() { ["sk"] = "sk-1" }, bump: new() { ["sk"] = AnalysisBump.Minor })));
    }

    [Fact]
    public void Identities_RenderEveryLanguage()
    {
        string source = AnalysisVersionPlanner.RenderIdentities(Versions());
        Assert.Contains("\"de\" => \"rowles-analysis/de/3.2.0\",", source, StringComparison.Ordinal);
        Assert.Contains("\"sk\" => \"rowles-analysis/sk/0.1.0\",", source, StringComparison.Ordinal);
        Assert.Contains("#nullable enable", source, StringComparison.Ordinal);
    }
}

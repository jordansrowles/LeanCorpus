using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Snowball;

namespace Rowles.LeanCorpus.DevOps.Tests.Snowball;

/// <summary>Identity inputs owned by the workspace: the shared runtime, the fingerprint corpus and code-only writes.</summary>
public sealed class SnowballWorkspaceTests
{
    [Fact]
    public void RuntimeChange_ChangesEveryCodeHash_AndNeedsADecision()
    {
        var copy = CopyWorkspace();
        File.AppendAllText(Path.Combine(copy.SnowballSourceRoot, "SnowballRuntime.cs"), "// edited\n");

        var exception = Assert.Throws<InvalidOperationException>(() => copy.Plan(AnalysisVersionRequests.None));
        Assert.Contains("changed without a Snowball version change", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FingerprintCorpusChange_FailsWithoutRecordedFingerprints()
    {
        var copy = CopyWorkspace();
        File.AppendAllText(copy.FingerprintCorpusPath, "sk\tnový riadok\n");

        var exception = Assert.Throws<InvalidOperationException>(() => copy.Plan(AnalysisVersionRequests.None));
        Assert.Contains("fingerprint corpus", exception.Message, StringComparison.Ordinal);
        Assert.Contains(copy.Verify(), static problem => problem.Contains("fingerprint corpus", StringComparison.Ordinal));
    }

    [Fact]
    public void FingerprintCorpusChange_IsRecordedWithTheFingerprints()
    {
        var copy = CopyWorkspace();
        File.AppendAllText(copy.FingerprintCorpusPath, "sk\tnový riadok\n");
        var current = AnalysisVersions.Parse(File.ReadAllText(copy.VersionsPath));
        var unchanged = current.Languages.Where(static language => language.Fingerprint is not null)
            .ToDictionary(static language => language.Code, static language => language.Fingerprint!, StringComparer.Ordinal);

        var generation = copy.Plan(AnalysisVersionRequests.None with { PendingFingerprints = unchanged });

        Assert.Equal(copy.FingerprintCorpusSha256(), generation.Versions.FingerprintCorpusSha256);
        Assert.NotEqual(current.FingerprintCorpusSha256, generation.Versions.FingerprintCorpusSha256);
    }

    [Fact]
    public void CodeOnly_WritesTheAlgorithms_AndLeavesTheIdentities()
    {
        var copy = CopyWorkspace();
        string versions = File.ReadAllText(copy.VersionsPath);
        string identities = Path.Combine(copy.GeneratedRoot, SnowballWorkspace.IdentitiesFile);
        File.WriteAllText(identities, "// placeholder\n");

        var written = copy.WriteCodeOnly();

        Assert.Equal(9, written.Count);
        Assert.DoesNotContain(SnowballWorkspace.IdentitiesFile, written);
        Assert.All(written, name => Assert.True(File.Exists(Path.Combine(copy.GeneratedRoot, name))));
        Assert.Equal(versions, File.ReadAllText(copy.VersionsPath));
        Assert.Equal("// placeholder\n", File.ReadAllText(identities));
    }

    [Theory]
    [InlineData("sk=5")]
    [InlineData("sk=-1")]
    [InlineData("sk=major")]
    public void UndefinedBumpValues_AreRejected(string value)
    {
        var options = new SnowballCommandOptions("generate", [], null, [], [value], AcceptFingerprints: true);
        var exception = Assert.Throws<ArgumentException>(() => SnowballCommandRequests.Parse(options, CopyWorkspace()));
        Assert.Contains("--bump", exception.Message, StringComparison.Ordinal);
    }

    private static SnowballWorkspace CopyWorkspace()
    {
        string root = RepositoryTemporaryStorage.NewPath("devops-tests", "snowball-workspace-");
        var source = new SnowballWorkspace(SnowballTransformTests.FindRepositoryRoot());
        var copy = new SnowballWorkspace(root);
        CopyDirectory(source.FixtureRoot, copy.FixtureRoot);
        Directory.CreateDirectory(copy.GeneratedRoot);
        File.Copy(source.VersionsPath, copy.VersionsPath);
        foreach (var runtime in SnowballWorkspace.RuntimeFiles)
            File.Copy(Path.Combine(source.SnowballSourceRoot, runtime), Path.Combine(copy.SnowballSourceRoot, runtime));
        Directory.CreateDirectory(Path.GetDirectoryName(copy.FingerprintCorpusPath)!);
        File.Copy(source.FingerprintCorpusPath, copy.FingerprintCorpusPath);
        return copy;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.Ordinal));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, destination, StringComparison.Ordinal));
    }
}

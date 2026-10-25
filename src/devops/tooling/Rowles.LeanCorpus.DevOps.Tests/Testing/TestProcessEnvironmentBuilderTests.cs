using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class TestProcessEnvironmentBuilderTests
{
    [Fact]
    public void AddsMatrixEnvironmentOverridesWithoutReplacingDevOpsRunIdentity()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "managed-test-environment"));
        var runId = Guid.NewGuid();
        var target = new TestTarget("core/net10.0", "Core", "core", "Mtp", "src/core.csproj", "net10.0",
            "Release", "", "", [], false, ["Trx"], []);
        var options = new TestRunnerOptions(root)
        {
            ArtifactsEnabled = true,
            Ci = true,
            EnvironmentOverrides = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["LEANCORPUS_BACKUP_RESTORE_MEASUREMENT"] = Path.Combine(root, "cell.json"),
                ["LEANCORPUS_BACKUP_COMPOUND"] = "true",
                ["CHAOS_ITERATIONS"] = "7"
            }
        };

        var variables = TestProcessEnvironmentBuilder.Build(options, runId, Path.Combine(root, "attempt"), target, 3);

        Assert.Equal(Path.Combine(root, "cell.json"), variables["LEANCORPUS_BACKUP_RESTORE_MEASUREMENT"]);
        Assert.Equal("true", variables["LEANCORPUS_BACKUP_COMPOUND"]);
        Assert.Equal("7", variables["CHAOS_ITERATIONS"]);
        Assert.Equal(runId.ToString("D"), variables["LEANCORPUS_RUN_ID"]);
        Assert.Equal("3", variables["LEANCORPUS_ITERATION"]);
    }

    [Fact(Explicit = true)]
    public void ManagedChildReceivesOverride()
        => Assert.Equal("managed-child-probe", Environment.GetEnvironmentVariable("LEANCORPUS_TEST_ENV_PROBE"));
}

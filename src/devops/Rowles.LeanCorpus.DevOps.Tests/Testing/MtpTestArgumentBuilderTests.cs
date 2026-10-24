using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class MtpTestArgumentBuilderTests
{
    [Fact]
    public void PreservesLegacyMtpFlagsAndDiagnosticsArguments()
    {
        using var directory = TemporaryDirectory.Create();
        var target = new TestTarget("core/net10.0", "Core", "core", "Mtp", "src/core.csproj", "net10.0", "Release",
            "", "Area=Search&Category=Unit", ["Search"], true, ["Trx", "HangDump", "CrashDump"], ["--custom", "value"]);
        var options = new TestRunnerOptions(directory.Path)
        {
            ArtifactsEnabled = true,
            Ci = true,
            CollectCoverage = true,
            Diagnostics = true,
            Flaky = true,
            FailWarnings = true,
            ParallelProfile = "stress",
            ExplicitMode = "only",
            Verbosity = "quiet",
            HangTimeout = "100s"
        };

        var arguments = MtpTestArgumentBuilder.Build(target, options, Path.Combine(directory.Path, "attempt"));

        AssertContainsPair(arguments, "--filter", "Area=Search&Category=Unit");
        AssertContainsPair(arguments, "--output", "Normal");
        AssertContainsPair(arguments, "--progress", "off");
        AssertContainsPair(arguments, "--results-directory", Path.Combine(directory.Path, "attempt"));
        Assert.Contains("--report-xunit-trx", arguments);
        AssertContainsPair(arguments, "--report-xunit-trx-filename", "results.trx");
        AssertContainsPair(arguments, "--xunit-config-filename", Path.Combine(directory.Path, "scripts", "devops", "testing", "testconfig.json"));
        AssertContainsPair(arguments, "--parallel", "all");
        AssertContainsPair(arguments, "--explicit", "only");
        AssertContainsPair(arguments, "--fail-warns", "on");
        Assert.Contains("--report-gh", arguments);
        Assert.Contains("--coverlet", arguments);
        AssertContainsPair(arguments, "--coverlet-output-format", "cobertura");
        AssertContainsPair(arguments, "--coverlet-file-prefix", "core-net10.0");
        AssertContainsPair(arguments, "--hangdump-timeout", "100s");
        AssertContainsPair(arguments, "--crashdump-type", "Mini");
        AssertContainsPair(arguments, "--diagnostic-output-directory", Path.Combine(directory.Path, "attempt", "diagnostics"));
        Assert.Equal(["--custom", "value"], arguments.TakeLast(2));
    }

    [Fact]
    public void OmitsCoverageForIneligibleTargetsAndHonoursDisabledHangDumps()
    {
        var target = new TestTarget("text/net10.0", "Text", "text", "Mtp", "src/text.csproj", "net10.0", "Release",
            "", "", [], false, ["Trx", "HangDump"], []);
        var options = new TestRunnerOptions(Path.GetTempPath())
        {
            ArtifactsEnabled = false,
            CollectCoverage = true,
            HangTimeout = "off",
            ExplicitMode = "off"
        };

        var arguments = MtpTestArgumentBuilder.Build(target, options, "");

        Assert.DoesNotContain("--coverlet", arguments);
        Assert.DoesNotContain("--hangdump", arguments);
        AssertContainsPair(arguments, "--explicit", "off");
        Assert.DoesNotContain("--results-directory", arguments);
    }

    [Fact]
    public void MatchesLegacyDefaultVerbosityAndExplicitModeArguments()
    {
        var target = new TestTarget("core/net10.0", "Core", "core", "Mtp", "src/core.csproj", "net10.0", "Release",
            "", "", [], false, [], []);
        var options = new TestRunnerOptions(Path.GetTempPath());

        var arguments = MtpTestArgumentBuilder.Build(target, options, "");

        Assert.DoesNotContain("--output", arguments);
        AssertContainsPair(arguments, "--parallel", "collections");
        AssertContainsPair(arguments, "--explicit", "off");
        Assert.DoesNotContain("--report-xunit-trx", arguments);
    }

    [Fact]
    public void ScopesLegacyArtifactAndTelemetryVariablesToTheChildProcess()
    {
        var options = new TestRunnerOptions(Path.GetTempPath()) { ArtifactsEnabled = true, Ci = true, Flaky = true };
        var target = new TestTarget("core/net10.0", "Core", "core", "Mtp", "src/core.csproj", "net10.0", "Release",
            "", "", [], true, [], []);
        var runId = Guid.Parse("7531d90b-82ad-4f01-8a3a-7f9a1750cfe8");

        var environment = TestProcessEnvironmentBuilder.Build(options, runId, "/tmp/test-attempt", target, 3);

        Assert.Equal("summary", environment["LEANCORPUS_TELEMETRY"]);
        Assert.Equal(runId.ToString("D"), environment["LEANCORPUS_RUN_ID"]);
        Assert.Equal("test", environment["LEANCORPUS_RUN_KIND"]);
        Assert.Equal("/tmp/test-attempt", environment["LEANCORPUS_ARTIFACT_DIR"]);
        Assert.Equal("core/net10.0", environment["LEANCORPUS_TARGET"]);
        Assert.Equal("core", environment["LEANCORPUS_SUITE"]);
        Assert.Equal("3", environment["LEANCORPUS_ITERATION"]);
        Assert.Equal("true", environment["LEANCORPUS_CI"]);
        Assert.Equal("false", environment["LEANCORPUS_DIAGNOSTICS"]);
    }

    private static void AssertContainsPair(IReadOnlyList<string> arguments, string option, string value)
    {
        var index = arguments.ToList().IndexOf(option);
        Assert.True(index >= 0, $"Missing option '{option}'.");
        Assert.True(index + 1 < arguments.Count, $"Missing value after '{option}'.");
        Assert.Equal(value, arguments[index + 1]);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;
        public string Path { get; }
        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LeanCorpus MTP arguments " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(path, "scripts", "devops", "testing"));
            return new TemporaryDirectory(path);
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

using Rowles.DataForge.Tool;

namespace Rowles.DataForge.Tests.Tool;

public sealed class CommandContractTests
{
    [Fact]
    public void Profiles_command_lists_identity_kind_defaults_and_description()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = DataForgeCommandLine.Run(["profiles"], output, error, Path.GetTempPath());
        var text = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("ID | Version | Kind | Default seed | Default count | Description", text, StringComparison.Ordinal);
        Assert.Contains("leancorpus-search | 1 | Generated | 42 | 20000", text, StringComparison.Ordinal);
        Assert.Contains("leancorpus-vector | 1 | Generated | 42 | 1000", text, StringComparison.Ordinal);
        Assert.Contains("leancorpus-hybrid | 1 | Generated | 42 | 20000", text, StringComparison.Ordinal);
        Assert.Contains("leancorpus-stress | 1 | Generated | 42 | 100000", text, StringComparison.Ordinal);
        Assert.Contains("rowles-text-multilingual | 1 | Generated | 42 | 256", text, StringComparison.Ordinal);
        Assert.Contains("leancorpus-e2e | 1 | Generated | 42 | 256", text, StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void Vector_profile_parameters_round_trip_through_manifest_and_reproduction()
    {
        var root = Path.Combine(Path.GetTempPath(), "dataforge-vector-cli-" + Guid.NewGuid().ToString("N"));
        var outputPath = Path.Combine(root, "vector");
        try
        {
            var generated = Run(root, "generate", "-Profile", "leancorpus-vector", "-Version", "1",
                "-Seed", "42", "-Count", "16", "-Dimension", "64",
                "-VectorDistribution", "Clustered", "-ClusterCount", "4", "-QueryCount", "2",
                "-Output", outputPath);
            Assert.Equal(0, generated.ExitCode);
            var inspected = Run(root, "inspect", outputPath);
            Assert.Equal(0, inspected.ExitCode);
            Assert.Contains("Parameter.dimension: 64", inspected.Output, StringComparison.Ordinal);
            Assert.Contains("Parameter.vectorDistribution: Clustered", inspected.Output, StringComparison.Ordinal);
            Assert.Equal(0, Run(root, "verify", outputPath).ExitCode);
            Assert.Equal(0, Run(root, "reproduce", outputPath).ExitCode);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Hybrid_profile_round_trips_through_manifest_and_reproduction()
    {
        var root = Path.Combine(Path.GetTempPath(), "dataforge-hybrid-cli-" + Guid.NewGuid().ToString("N"));
        var outputPath = Path.Combine(root, "hybrid");
        try
        {
            var generated = Run(root, "generate", "-Profile", "leancorpus-hybrid", "-Version", "1",
                "-Seed", "42", "-Count", "16", "-Dimension", "64", "-Output", outputPath);
            Assert.Equal(0, generated.ExitCode);
            Assert.Contains("Parameter.dimension: 64", Run(root, "inspect", outputPath).Output, StringComparison.Ordinal);
            Assert.Equal(0, Run(root, "verify", outputPath).ExitCode);
            Assert.Equal(0, Run(root, "reproduce", outputPath).ExitCode);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("search")]
    [InlineData("vector")]
    [InlineData("hybrid")]
    public void Stress_modes_round_trip_with_replay_identity(string mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "dataforge-stress-cli-" + Guid.NewGuid().ToString("N"));
        var outputPath = Path.Combine(root, mode);
        try
        {
            var generated = Run(root, "generate", "-Profile", "leancorpus-stress", "-Mode", mode,
                "-Count", "12", "-Output", outputPath);
            Assert.Equal(0, generated.ExitCode);
            var inspected = Run(root, "inspect", outputPath);
            Assert.Contains("Parameter.mode: " + mode, inspected.Output, StringComparison.Ordinal);
            Assert.Equal(0, Run(root, "verify", outputPath).ExitCode);
            Assert.Equal(0, Run(root, "reproduce", outputPath).ExitCode);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Search_stress_limit_requires_allow_large_and_reports_replay_identity()
    {
        var blocked = Run(Path.GetTempPath(), "generate", "-Profile", "leancorpus-stress",
            "-Mode", "search", "-Count", "1000001", "-CategoryCardinality", "1024");
        Assert.Equal(2, blocked.ExitCode);
        Assert.Contains("leancorpus-stress/v1 seed=42 count=1000001 parameters=[categoryCardinality=1024,mode=search]",
            blocked.Error, StringComparison.Ordinal);
        Assert.Contains("-AllowLarge", blocked.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_stress_parameters_report_complete_replay_identity()
    {
        var invalid = Run(Path.GetTempPath(), "generate", "-Profile", "leancorpus-stress",
            "-Mode", "hybrid", "-Count", "12", "-RegionCardinality", "256");
        Assert.Equal(2, invalid.ExitCode);
        Assert.Contains("leancorpus-stress/v1 seed=42 count=12 parameters=[mode=hybrid,regionCardinality=256]",
            invalid.Error, StringComparison.Ordinal);
        Assert.Contains("Unknown hybrid stress parameter", invalid.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Multilingual_languages_round_trip_through_manifest_and_reproduction()
    {
        var root = Path.Combine(Path.GetTempPath(), "dataforge-text-cli-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var language in Rowles.DataForge.Workloads.RowlesTextMultilingualProfile.Languages)
            {
                var path = Path.Combine(root, language);
                var generated = Run(root, "generate", "-Profile", "rowles-text-multilingual",
                    "-Language", language, "-Count", "16", "-Output", path);
                Assert.Equal(0, generated.ExitCode);
                Assert.Contains("Parameter.language: " + language, Run(root, "inspect", path).Output, StringComparison.Ordinal);
                Assert.Equal(0, Run(root, "verify", path).ExitCode);
                Assert.Equal(0, Run(root, "reproduce", path).ExitCode);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void E2e_profile_round_trips_at_default_count()
    {
        var root = Path.Combine(Path.GetTempPath(), "dataforge-e2e-cli-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "e2e");
        try
        {
            var generated = Run(root, "generate", "-Profile", "leancorpus-e2e", "-Output", path);
            Assert.Equal(0, generated.ExitCode);
            Assert.Contains("Count: 256", generated.Output, StringComparison.Ordinal);
            Assert.Equal(0, Run(root, "verify", path).ExitCode);
            Assert.Equal(0, Run(root, "reproduce", path).ExitCode);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Generate_inspect_verify_and_reproduce_commands_work_together()
    {
        var root = Path.Combine(Path.GetTempPath(), "dataforge-cli-" + Guid.NewGuid().ToString("N"));
        var outputPath = Path.Combine(root, "generated", "sample");
        try
        {
            var generated = Run(root, "generate", "-Profile", "leancorpus-search", "-Version", "1", "-Seed", "42", "-Count", "12", "-Output", outputPath);
            Assert.Equal(0, generated.ExitCode);
            Assert.Contains("Profile: leancorpus-search", generated.Output, StringComparison.Ordinal);
            Assert.Contains("ContentSha256:", generated.Output, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(outputPath, "manifest.json")));

            var inspected = Run(root, "inspect", outputPath);
            Assert.Equal(0, inspected.ExitCode);
            Assert.Contains("Count: 12", inspected.Output, StringComparison.Ordinal);

            var verified = Run(root, "verify", outputPath);
            Assert.Equal(0, verified.ExitCode);
            Assert.Contains("Verified: true", verified.Output, StringComparison.Ordinal);

            var reproduced = Run(root, "reproduce", outputPath);
            Assert.Equal(0, reproduced.ExitCode);
            Assert.Contains("Reproduced: true", reproduced.Output, StringComparison.Ordinal);

            var refusedOverwrite = Run(root, "generate", "-Profile", "leancorpus-search", "-Version", "1", "-Seed", "42", "-Count", "12", "-Output", outputPath);
            Assert.Equal(1, refusedOverwrite.ExitCode);

            var forced = Run(root, "generate", "-Profile", "leancorpus-search", "-Version", "1", "-Seed", "42", "-Count", "12", "-Output", outputPath, "-Force");
            Assert.Equal(0, forced.ExitCode);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Invalid_commands_and_output_path_traversal_return_exit_code_two()
    {
        var root = Path.Combine(Path.GetTempPath(), "dataforge-cli-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var unknown = Run(root, "unknown");
            Assert.Equal(2, unknown.ExitCode);
            Assert.Contains("Unknown DataForge command", unknown.Error, StringComparison.Ordinal);

            var outsideName = Path.GetFileName(root) + "-outside";
            var traversal = Run(root, "generate", "-Profile", "leancorpus-search", "-Version", "1", "-Seed", "42", "-Count", "3", "-Output", "../" + outsideName);
            Assert.Equal(2, traversal.ExitCode);
            Assert.Contains("parent-directory traversal", traversal.Error, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(root)!, outsideName)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static (int ExitCode, string Output, string Error) Run(string root, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = DataForgeCommandLine.Run(args, output, error, root);
        return (exitCode, output.ToString(), error.ToString());
    }
}

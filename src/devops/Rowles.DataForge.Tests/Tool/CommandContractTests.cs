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
        Assert.Empty(error.ToString());
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

using Rowles.LeanCorpus.DevOps.Commands;

namespace Rowles.LeanCorpus.DevOps.Tests.Commands;

public sealed class ManagedCommandHandlerCancellationTests
{
    [Fact]
    public void Clean_ForwardsOperationCancellationToken()
    {
        string repositoryRoot = Path.Combine(Path.GetTempPath(), "leancorpus-command-cancellation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "src"));
        File.WriteAllText(Path.Combine(repositoryRoot, "global.json"), "{}");
        File.WriteAllText(Path.Combine(repositoryRoot, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(repositoryRoot, "src", "Rowles.LeanCorpus.slnx"), "<Solution />");

        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            using var output = new StringWriter();
            using var error = new StringWriter();
            var handler = new ManagedCommandHandler(output, error, cancellation.Token);

            int exitCode = handler.Clean(new CleanCommandOptions("default", repositoryRoot));

            Assert.Equal(130, exitCode);
            Assert.Contains("cancel", output.ToString() + error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
                Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidateConfiguration_ForwardsOperationCancellationTokenToSolutionInventory()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var handler = new ManagedCommandHandler(output, error, cancellation.Token);

        int exitCode = handler.ValidateConfiguration(new ConfigValidateCommandOptions(
            RepositoryPath: FindRepositoryRoot(),
            SolutionInventory: true));

        Assert.Equal(130, exitCode);
        Assert.Contains("cancel", output.ToString() + error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        foreach (var startPath in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(startPath));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "Rowles.LeanCorpus.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the LeanCorpus repository root for the cancellation test.");
    }
}

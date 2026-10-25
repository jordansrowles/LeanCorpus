using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Commands;

public sealed class ManagedTestCommandHandlerTests
{
    [Fact]
    public void ListIgnoresUnknownSuiteSelection()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var checkout = RepositoryPaths.Resolve(null, Environment.CurrentDirectory, Environment.ProcessPath);
        var handler = new ManagedCommandHandler(output, error);
        var options = new TestCommandOptions(new TestRunOptions
        {
            Selection = new TestTargetRequest { Suites = ["not-registered"] }
        }, checkout.RepositoryRoot, List: true);

        var exitCode = handler.Test(options);

        Assert.Equal(0, exitCode);
        Assert.Contains("Available test suites", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("core", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }
}

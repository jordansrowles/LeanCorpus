using Rowles.LeanCorpus.DevOps.Data;

namespace Rowles.LeanCorpus.DevOps.Tests.Data;

public sealed class DataForgeCommandServiceTests
{
    [Fact]
    public void BuildsAReleaseDotnetRunCommandWithoutChangingToolArgumentBoundaries()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "LeanCorpus checkout");
        string[] toolArguments = ["generate", "-Profile", "leancorpus-search", "-Output", "data set"];

        var arguments = DataForgeCommandService.BuildArguments(repositoryRoot, toolArguments);

        Assert.Equal("run", arguments[0]);
        Assert.Equal("--project", arguments[1]);
        Assert.Equal(Path.Combine(repositoryRoot, "src", "devops", "dataforge", "Rowles.DataForge.Tool", "Rowles.DataForge.Tool.csproj"), arguments[2]);
        var expectedTail = new[] { "--framework", "net10.0", "--configuration", "Release", "--" }.Concat(toolArguments);
        Assert.Equal(expectedTail, arguments.Skip(3));
    }
}

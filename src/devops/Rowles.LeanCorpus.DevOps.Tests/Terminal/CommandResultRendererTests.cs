using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Terminal;

public sealed class CommandResultRendererTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(3, 3)]
    [InlineData(4, 124)]
    [InlineData(5, 130)]
    public void MapsOutcomeToStableExitCode(int outcomeValue, int expectedExitCode)
    {
        var result = new CommandResult((OutcomeKind)outcomeValue, "result");

        Assert.Equal(expectedExitCode, result.ExitCode);
    }

    [Fact]
    public void PlainRendererOwnsStdoutForSuccessAndStderrForFailure()
    {
        var renderer = new CommandResultRenderer();
        var successOutput = new StringWriter();
        var successError = new StringWriter();
        var failureOutput = new StringWriter();
        var failureError = new StringWriter();

        Assert.Equal(0, renderer.Render(new CommandResult(OutcomeKind.Success, "done"), successOutput, successError));
        Assert.Equal(2, renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, "invalid", Details: "missing option"), failureOutput, failureError));

        Assert.Equal("done" + Environment.NewLine, successOutput.ToString());
        Assert.Empty(successError.ToString());
        Assert.Empty(failureOutput.ToString());
        Assert.Contains("invalid" + Environment.NewLine + "missing option", failureError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void JsonRendererWritesOneStructuredResultToStdoutAndPreservesRawChildExit()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var result = new CommandResult(OutcomeKind.CommandFailure, "child failed", RawChildExitCode: 23);

        var exitCode = new CommandResultRenderer().Render(result, output, error, CommandOutputFormat.Json);
        using var json = JsonDocument.Parse(output.ToString());

        Assert.Equal(1, exitCode);
        Assert.Empty(error.ToString());
        Assert.Equal("CommandFailure", json.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(23, json.RootElement.GetProperty("rawChildExitCode").GetInt32());
    }
}

using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Terminal;

internal sealed class CommandResultRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public int Render(CommandResult result, TextWriter standardOutput, TextWriter standardError, CommandOutputFormat format = CommandOutputFormat.Plain)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (format == CommandOutputFormat.Json)
        {
            standardOutput.WriteLine(JsonSerializer.Serialize(new
            {
                outcome = result.Outcome.ToString(),
                exitCode = result.ExitCode,
                rawChildExitCode = result.RawChildExitCode,
                message = result.Message,
                details = result.Details
            }, JsonOptions));
        }
        else if (result.Outcome == OutcomeKind.Success)
        {
            standardOutput.WriteLine(result.Message);
        }
        else
        {
            standardError.WriteLine(result.Message);
            if (!string.IsNullOrWhiteSpace(result.Details))
            {
                standardError.WriteLine(result.Details);
            }
        }

        return result.ExitCode;
    }
}

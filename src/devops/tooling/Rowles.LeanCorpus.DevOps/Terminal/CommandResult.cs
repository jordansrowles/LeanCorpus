namespace Rowles.LeanCorpus.DevOps.Terminal;

internal sealed record CommandResult(
    OutcomeKind Outcome,
    string Message,
    int? RawChildExitCode = null,
    string? Details = null)
{
    public int ExitCode => Outcome switch
    {
        OutcomeKind.Success => 0,
        OutcomeKind.ValidationFailure => 2,
        OutcomeKind.CommandFailure => 1,
        OutcomeKind.InfrastructureFailure => 3,
        OutcomeKind.Timeout => 124,
        OutcomeKind.Cancelled => 130,
        _ => throw new ArgumentOutOfRangeException(nameof(Outcome), Outcome, "Unknown command outcome.")
    };
}

internal enum CommandOutputFormat
{
    Plain,
    Json
}

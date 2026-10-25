using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Infrastructure;

internal sealed record ProcessResult(
    OutcomeKind Outcome,
    int? RawExitCode,
    IReadOnlyList<string> StandardOutputTail,
    IReadOnlyList<string> StandardErrorTail,
    int StandardOutputLineCount,
    int StandardErrorLineCount,
    TimeSpan Duration,
    string StandardOutputLogPath,
    string StandardErrorLogPath,
    string? Failure,
    bool StoppedByCondition = false);

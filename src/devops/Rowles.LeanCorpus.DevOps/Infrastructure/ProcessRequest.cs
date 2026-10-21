namespace Rowles.LeanCorpus.DevOps.Infrastructure;

internal sealed record ProcessRequest
{
    public required string ExecutablePath { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string StandardOutputLogPath { get; init; }

    public required string StandardErrorLogPath { get; init; }

    public TimeSpan? Timeout { get; init; }

    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromSeconds(5);

    public int MaxTailLines { get; init; } = 200;

    public int MaxTailCharacters { get; init; } = 16_384;

    public Action<ProcessProgressEvent>? Progress { get; init; }

    public string? StopOnStandardOutputPrefix { get; init; }
}

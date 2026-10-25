namespace Rowles.LeanCorpus.DevOps.Artifacts;

internal enum RunState
{
    Created,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
    Interrupted
}

internal sealed record RunManifest
{
    public const int CurrentSchemaVersion = 1;

    public required int SchemaVersion { get; init; }

    public required Guid RunId { get; init; }

    public Guid? ParentRunId { get; init; }

    public required string Command { get; init; }

    public required RunState State { get; init; }

    public required DateTimeOffset StartedUtc { get; init; }

    public DateTimeOffset? CompletedUtc { get; init; }

    public string? SourceCommit { get; init; }

    public string? SourceBranch { get; init; }

    public required bool SourceDirty { get; init; }

    public required string Platform { get; init; }

    public required IReadOnlyDictionary<string, string> Inputs { get; init; }

    public required IReadOnlyList<int> ChildExitCodes { get; init; }

    public required IReadOnlyList<string> Artifacts { get; init; }

    public IReadOnlyDictionary<string, string> ToolVersions { get; init; } = new Dictionary<string, string>();

    public int? OwnerProcessId { get; init; }

    public DateTimeOffset? OwnerProcessStartedUtc { get; init; }

    public string? Failure { get; init; }
}

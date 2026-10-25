namespace Rowles.LeanCorpus.DevOps.Workflows;

internal enum WorkflowTargetState
{
    Started,
    Succeeded,
    Failed,
    Cancelled,
    Skipped
}

internal sealed record WorkflowProgressEvent(
    string Target,
    WorkflowTargetState State,
    DateTimeOffset OccurredUtc,
    string? Message = null);

internal sealed record WorkflowTarget(
    string Name,
    IReadOnlyList<string> DependsOn,
    Func<CancellationToken, Task> Action,
    string Description);

internal sealed record WorkflowResult(
    Rowles.LeanCorpus.DevOps.Terminal.OutcomeKind Outcome,
    IReadOnlyList<WorkflowProgressEvent> Events,
    IReadOnlyList<string> FailedTargets,
    IReadOnlyList<string> SkippedTargets,
    string? Failure);

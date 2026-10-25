namespace Rowles.LeanCorpus.DevOps.Terminal;

internal enum OutcomeKind
{
    Success,
    ValidationFailure,
    CommandFailure,
    InfrastructureFailure,
    Timeout,
    Cancelled
}

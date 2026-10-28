namespace Rowles.LeanCorpus.Server.Core.Execution;

internal enum LocalIndexHandleLifecycleState
{
    Active,
    Closing,
    Closed,
    Failed
}

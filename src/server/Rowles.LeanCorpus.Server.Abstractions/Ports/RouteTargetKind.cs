namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Identifies where an operation should execute.</summary>
public enum RouteTargetKind
{
    /// <summary>Execute in the local process.</summary>
    Local,
    /// <summary>Execute on another node.</summary>
    Remote,
    /// <summary>Reject the operation.</summary>
    Rejected
}

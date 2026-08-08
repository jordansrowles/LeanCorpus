namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Specifies the caller's read-consistency requirement.</summary>
public enum RequestedConsistency
{
    /// <summary>Read the local copy.</summary>
    Local,
    /// <summary>Read after a quorum condition.</summary>
    Quorum,
    /// <summary>Read with linearisable semantics.</summary>
    Linearisable
}

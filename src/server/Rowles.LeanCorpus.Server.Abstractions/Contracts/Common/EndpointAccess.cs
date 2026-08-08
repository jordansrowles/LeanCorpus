namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

/// <summary>Classifies whether an endpoint requires administrative policy.</summary>
public enum EndpointAccess
{
    /// <summary>Ordinary public operation.</summary>
    Public,

    /// <summary>Administrative operation.</summary>
    Administrative,
}

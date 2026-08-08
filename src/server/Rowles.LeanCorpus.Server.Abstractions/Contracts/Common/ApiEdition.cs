namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

/// <summary>Classifies the product edition that owns an endpoint or capability.</summary>
public enum ApiEdition
{
    /// <summary>Available in Community and Enterprise hosts.</summary>
    Community,

    /// <summary>Available only when an Enterprise module registers it.</summary>
    Enterprise,
}

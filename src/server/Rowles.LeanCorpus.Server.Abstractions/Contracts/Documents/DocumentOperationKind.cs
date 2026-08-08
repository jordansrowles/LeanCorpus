namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;

/// <summary>Specifies one document operation in a streaming bulk request.</summary>
public enum DocumentOperationKind
{
    /// <summary>Index or replace a document.</summary>
    Index,
    /// <summary>Apply an update defined by the host.</summary>
    Update,
    /// <summary>Delete a document.</summary>
    Delete,
}

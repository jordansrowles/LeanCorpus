namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

/// <summary>Identifies a field type supported by the server schema contract.</summary>
public enum IndexFieldType
{
    /// <summary>Full-text field.</summary>
    Text,
    /// <summary>Exact string field.</summary>
    Keyword,
    /// <summary>Signed 64-bit integer field.</summary>
    Int64,
    /// <summary>Double-precision numeric field.</summary>
    Double,
    /// <summary>Boolean field.</summary>
    Boolean,
    /// <summary>UTC date and time field.</summary>
    DateTime,
    /// <summary>Binary field.</summary>
    Binary,
    /// <summary>Dense floating-point vector field.</summary>
    Vector,
}

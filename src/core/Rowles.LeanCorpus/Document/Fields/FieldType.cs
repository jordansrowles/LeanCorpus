namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>Categorises the kind of data a field holds.</summary>
public enum FieldType
{
    /// <summary>Exact-match string, not analysed.</summary>
    String,

    /// <summary>Full-text content, passed through the analyser pipeline.</summary>
    Text,

    /// <summary>Opaque binary content stored as raw bytes.</summary>
    Binary,

    /// <summary>Numeric value for range filters and sorting.</summary>
    Numeric,

    /// <summary>Dense float vector for semantic search.</summary>
    Vector,

    /// <summary>Stored-only value, not included in the inverted index.</summary>
    Stored
}

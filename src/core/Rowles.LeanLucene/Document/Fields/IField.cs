namespace Rowles.LeanLucene.Document.Fields;

/// <summary>Defines the contract for a document field.</summary>
public interface IField
{
    /// <summary>Field name used for indexing and retrieval.</summary>
    string Name { get; }

    /// <summary>The kind of data this field holds.</summary>
    FieldType FieldType { get; }

    /// <summary>Whether the raw value is persisted in stored fields.</summary>
    bool IsStored { get; }

    /// <summary>Whether the field is included in the inverted index.</summary>
    bool IsIndexed { get; }
}

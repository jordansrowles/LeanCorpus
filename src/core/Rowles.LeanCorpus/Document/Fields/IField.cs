namespace Rowles.LeanCorpus.Document.Fields;

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

    /// <summary>Index-time boost applied to queries that score this field.</summary>
    float Boost { get; }

    /// <summary>
    /// Whether to populate DocValues (sorted, sorted-set, numeric, binary) for this field.
    /// When <c>false</c>, no DocValues sidecar files are produced for the field.
    /// Default: <c>true</c> for <see cref="StringField"/> and <see cref="NumericField"/>;
    /// <c>false</c> for <see cref="TextField"/>, <see cref="StoredField"/>, and <see cref="BinaryField"/>.
    /// </summary>
    bool StoreDocValues { get; }
}

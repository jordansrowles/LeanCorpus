using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Mapping;

/// <summary>
/// Strongly typed field descriptor produced by the LeanCorpus source generator.
/// Carries the field name, runtime <see cref="FieldType"/>, and persistence flags
/// so that callers can build queries and sorts without restating field names.
/// </summary>
/// <typeparam name="TDocument">The mapped document model type.</typeparam>
/// <typeparam name="TValue">The CLR value type for this field.</typeparam>
public sealed class LeanField<TDocument, TValue>
{
    /// <summary>
    /// Initialises a new <see cref="LeanField{TDocument, TValue}"/>.
    /// </summary>
    /// <param name="name">The field name used at indexing time.</param>
    /// <param name="fieldType">The runtime <see cref="FieldType"/> emitted for this field.</param>
    /// <param name="isStored">Whether the field is persisted in stored fields.</param>
    /// <param name="isIndexed">Whether the field is included in the inverted index.</param>
    /// <param name="isRequired">Whether the field is required by the generated schema.</param>
    public LeanField(string name, FieldType fieldType, bool isStored, bool isIndexed, bool isRequired)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Field names must not be null or empty.", nameof(name));

        Name = name;
        FieldType = fieldType;
        IsStored = isStored;
        IsIndexed = isIndexed;
        IsRequired = isRequired;
    }

    /// <summary>The field name used at indexing time.</summary>
    public string Name { get; }

    /// <summary>The runtime <see cref="FieldType"/> emitted for this field.</summary>
    public FieldType FieldType { get; }

    /// <summary>Whether the field is persisted in stored fields.</summary>
    public bool IsStored { get; }

    /// <summary>Whether the field is included in the inverted index.</summary>
    public bool IsIndexed { get; }

    /// <summary>Whether the field is required by the generated schema.</summary>
    public bool IsRequired { get; }

    /// <summary>Returns the field name; convenience for query helpers.</summary>
    /// <returns>The field name.</returns>
    public override string ToString() => Name;
}

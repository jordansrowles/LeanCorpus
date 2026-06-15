using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Linq;

namespace Rowles.LeanCorpus.Mapping;

/// <summary>
/// Field metadata for a <see cref="LeanDocumentMap{TDocument}"/>. The non-generic value type
/// makes the binding collection iterable without knowing each field's CLR type.
/// </summary>
/// <typeparam name="TDocument">The mapped document model type.</typeparam>
public sealed class LeanFieldBinding<TDocument> : IFieldDescriptor
{
    /// <summary>
    /// Initialises a new <see cref="LeanFieldBinding{TDocument}"/>.
    /// </summary>
    /// <param name="name">The field name used at indexing time.</param>
    /// <param name="fieldType">The runtime <see cref="FieldType"/> emitted for this field.</param>
    /// <param name="isStored">Whether the field is persisted in stored fields.</param>
    /// <param name="isIndexed">Whether the field is included in the inverted index.</param>
    /// <param name="isRequired">Whether the field is required by the generated schema.</param>
    public LeanFieldBinding(string name, FieldType fieldType, bool isStored, bool isIndexed, bool isRequired)
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
}

using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Linq;

/// <summary>
/// Non-generic field metadata consumed by the expression-tree visitor
/// when translating LINQ member accesses into <see cref="Search.Query"/> objects.
/// Both <see cref="Mapping.LeanField{TDoc,TVal}"/> and
/// <see cref="Mapping.LeanFieldBinding{TDoc}"/> implement this interface,
/// so the resolver delegate can return either.
/// </summary>
public interface IFieldDescriptor
{
    /// <summary>The field name used at indexing time.</summary>
    string Name { get; }

    /// <summary>The runtime <see cref="FieldType"/> for this field.</summary>
    FieldType FieldType { get; }

    /// <summary>Whether the field is persisted in stored fields.</summary>
    bool IsStored { get; }

    /// <summary>Whether the field is included in the inverted index.</summary>
    bool IsIndexed { get; }

    /// <summary>Whether the field is required by the generated schema.</summary>
    bool IsRequired { get; }
}

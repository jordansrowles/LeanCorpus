using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Mapping.Attributes;

/// <summary>
/// Maps a stored-only property to either a <see cref="StoredField"/> (for <see cref="string"/>)
/// or a <see cref="BinaryField"/> (for <see cref="T:byte[]"/>). The field is not indexed.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class LeanStoredAttribute : Attribute
{
    /// <summary>
    /// Initialises a new <see cref="LeanStoredAttribute"/>.
    /// </summary>
    /// <param name="name">The field name used at indexing time.</param>
    public LeanStoredAttribute(string name)
    {
        Name = name;
    }

    /// <summary>The field name used at indexing time.</summary>
    public string Name { get; }

    /// <summary>Whether the field is required by the generated schema.</summary>
    public bool Required { get; init; }
}

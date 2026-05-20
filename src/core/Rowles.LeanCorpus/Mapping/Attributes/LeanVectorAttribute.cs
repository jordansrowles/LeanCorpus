using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Mapping.Attributes;

/// <summary>
/// Maps a dense float vector property to a <see cref="VectorField"/>. The property type must be
/// <see cref="float"/>[], and <see cref="Dimension"/> must be set to the expected length.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class LeanVectorAttribute : Attribute
{
    /// <summary>
    /// Initialises a new <see cref="LeanVectorAttribute"/>.
    /// </summary>
    /// <param name="name">The field name used at indexing time.</param>
    public LeanVectorAttribute(string name)
    {
        Name = name;
    }

    /// <summary>The field name used at indexing time.</summary>
    public string Name { get; }

    /// <summary>The required dimension of every vector value. Must be set.</summary>
    public int Dimension { get; init; }

    /// <summary>Whether the field is required by the generated schema.</summary>
    public bool Required { get; init; }
}

using Rowles.LeanLucene.Analysis.Analysers;

namespace Rowles.LeanLucene.Index.Indexer;

/// <summary>
/// Describes the expected type and behaviour of a single field in an <see cref="IndexSchema"/>.
/// </summary>
public sealed class FieldMapping
{
    /// <summary>The field name this mapping applies to.</summary>
    public string Name { get; }

    /// <summary>The expected field type.</summary>
    public FieldType FieldType { get; }

    /// <summary>Optional analyser override for this field. Null means use the schema/writer default.</summary>
    public IAnalyser? Analyser { get; init; }

    /// <summary>Whether the field value should be stored. Default: false.</summary>
    public bool IsStored { get; init; }

    /// <summary>Whether the field should be indexed. Default: true.</summary>
    public bool IsIndexed { get; init; } = true;

    /// <summary>Whether the field is required on every document. Default: false.</summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// Initialises a new <see cref="FieldMapping"/> for the specified field name and type.
    /// </summary>
    /// <param name="name">The field name this mapping applies to. Must not be null or empty.</param>
    /// <param name="fieldType">The expected field type for validation.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="name"/> is null or empty.</exception>
    public FieldMapping(string name, FieldType fieldType)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        FieldType = fieldType;
    }
}

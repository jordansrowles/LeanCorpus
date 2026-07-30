namespace Rowles.LeanCorpus.Mapping.Attributes;

/// <summary>Maps a <see cref="float"/>[][] token-embedding property to a <see cref="Document.Fields.MultiVectorField"/>.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class LeanMultiVectorAttribute : Attribute
{
    /// <summary>Initialises a multi-vector mapping.</summary>
    public LeanMultiVectorAttribute(string name) => Name = name;

    /// <summary>The field name used at indexing time.</summary>
    public string Name { get; }

    /// <summary>The required dimension of every token vector.</summary>
    public int Dimension { get; init; }

    /// <summary>Whether the field is required by the generated schema.</summary>
    public bool Required { get; init; }
}

using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Mapping.Attributes;

/// <summary>
/// Maps a numeric, temporal, or decimal property to a <see cref="NumericField"/>
/// (or a <see cref="StoredField"/> when <see cref="Encoding"/> is
/// <see cref="LeanNumericEncoding.DecimalAsString"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class LeanNumericAttribute : Attribute
{
    /// <summary>
    /// Initialises a new <see cref="LeanNumericAttribute"/>.
    /// </summary>
    /// <param name="name">The field name used at indexing time.</param>
    public LeanNumericAttribute(string name)
    {
        Name = name;
    }

    /// <summary>The field name used at indexing time.</summary>
    public string Name { get; }

    /// <summary>Whether the value is persisted in stored fields. Defaults to <c>true</c>.</summary>
    public bool Stored { get; init; } = true;

    /// <summary>Whether the field is required by the generated schema.</summary>
    public bool Required { get; init; }

    /// <summary>
    /// The encoding used when projecting non-numeric CLR types such as <see cref="DateTimeOffset"/>,
    /// <see cref="DateOnly"/>, <see cref="TimeOnly"/>, and <see cref="decimal"/>.
    /// </summary>
    public LeanNumericEncoding Encoding { get; init; } = LeanNumericEncoding.None;
}

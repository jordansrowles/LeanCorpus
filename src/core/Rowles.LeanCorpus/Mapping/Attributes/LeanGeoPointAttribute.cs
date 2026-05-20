using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Mapping.Attributes;

/// <summary>
/// Maps a <see cref="LeanGeoLocation"/> property to a <see cref="GeoPointField"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class LeanGeoPointAttribute : Attribute
{
    /// <summary>
    /// Initialises a new <see cref="LeanGeoPointAttribute"/>.
    /// </summary>
    /// <param name="name">The field name used at indexing time.</param>
    public LeanGeoPointAttribute(string name)
    {
        Name = name;
    }

    /// <summary>The field name used at indexing time.</summary>
    public string Name { get; }

    /// <summary>Whether the field is required by the generated schema.</summary>
    public bool Required { get; init; }
}

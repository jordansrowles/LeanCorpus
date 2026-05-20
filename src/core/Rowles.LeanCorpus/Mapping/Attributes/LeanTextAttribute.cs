using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Mapping.Attributes;

/// <summary>
/// Maps a <see cref="string"/> property to a <see cref="TextField"/>. The value passes through
/// the analyser pipeline at indexing time.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class LeanTextAttribute : Attribute
{
    /// <summary>
    /// Initialises a new <see cref="LeanTextAttribute"/>.
    /// </summary>
    /// <param name="name">The field name used at indexing time.</param>
    public LeanTextAttribute(string name)
    {
        Name = name;
    }

    /// <summary>The field name used at indexing time.</summary>
    public string Name { get; }

    /// <summary>Whether the original value is persisted in stored fields. Defaults to <c>true</c>.</summary>
    public bool Stored { get; init; } = true;

    /// <summary>Whether the field is required by the generated schema.</summary>
    public bool Required { get; init; }
}

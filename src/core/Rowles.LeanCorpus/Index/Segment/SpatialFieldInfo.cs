using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;
using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>Describes the coordinate family and value format of a spatial segment field.</summary>
public sealed class SpatialFieldInfo
{
    /// <summary>Gets the indexed field name.</summary>
    public string FieldName { get; init; } = string.Empty;

    /// <summary>Gets the spatial value kind stored for the field.</summary>
    public SpatialFieldKind Kind { get; init; }

    internal void Validate()
    {
        try
        {
            FieldNameValidator.Validate(FieldName, nameof(FieldName));
            PackedBkdFormat.ValidateFieldName(FieldName);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("Spatial field metadata contains an invalid field name.", ex);
        }

        if (!Enum.IsDefined(Kind))
            throw new InvalidDataException($"Spatial field '{FieldName}' has an undefined kind value '{Kind}'.");
    }
}

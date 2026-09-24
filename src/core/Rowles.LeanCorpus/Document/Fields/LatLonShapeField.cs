using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>An indexed geographic point, line, rectangle, polygon or geometry collection.</summary>
/// <remarks>
/// Shape primitives are written to Packed BKD. This field is not stored and has no DocValues in 3.2.
/// Circles are query-only and cannot be indexed.
/// </remarks>
public sealed class LatLonShapeField : IField
{
    /// <summary>Creates a geographic shape field.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="geometry">A supported built-in geographic geometry.</param>
    /// <param name="boost">The index-time field boost.</param>
    public LatLonShapeField(string name, IGeoGeometry geometry, float boost = 1.0f)
    {
        Name = FieldNameValidator.Validate(name, nameof(name));
        ArgumentNullException.ThrowIfNull(geometry);
        ShapeTessellator.ValidateGeoFieldGeometry(geometry);
        Geometry = geometry;
        Boost = FieldBoostValidator.Validate(boost, nameof(boost));
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Gets the indexed geographic geometry.</summary>
    public IGeoGeometry Geometry { get; }

    /// <inheritdoc/>
    public float Boost { get; }

    /// <inheritdoc/>
    public bool IsIndexed => true;

    /// <inheritdoc/>
    public bool IsStored => false;

    /// <inheritdoc/>
    public bool StoreDocValues => false;

    /// <inheritdoc/>
    public FieldIndexOptions IndexOptions => FieldIndexOptions.DocsOnly;

    /// <inheritdoc/>
    public FieldType FieldType => FieldType.Binary;
}

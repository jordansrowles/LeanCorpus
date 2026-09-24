using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>An indexed Cartesian point, line, rectangle, polygon or geometry collection.</summary>
/// <remarks>
/// Shape primitives are written to Packed BKD. This field is not stored and has no DocValues in 3.2.
/// Circles are query-only and cannot be indexed.
/// </remarks>
public sealed class XYShapeField : IField
{
    /// <summary>Creates a Cartesian shape field.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="geometry">A supported built-in Cartesian geometry.</param>
    /// <param name="boost">The index-time field boost.</param>
    public XYShapeField(string name, IXYGeometry geometry, float boost = 1.0f)
    {
        Name = FieldNameValidator.Validate(name, nameof(name));
        ArgumentNullException.ThrowIfNull(geometry);
        ShapeTessellator.ValidateXYFieldGeometry(geometry);
        Geometry = geometry;
        Boost = FieldBoostValidator.Validate(boost, nameof(boost));
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Gets the indexed Cartesian geometry.</summary>
    public IXYGeometry Geometry { get; }

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

using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>An indexed Cartesian point, line, rectangle, polygon or geometry collection.</summary>
/// <remarks>
/// Quantised shape primitives are indexed in Packed BKD and, by default, Shape DocValues.
/// Shape DocValues retain operational geometry metadata and are not source-geometry or WKT storage.
/// Circles are query-only and cannot be indexed.
/// </remarks>
public sealed class XYShapeField : IField
{
    /// <summary>Creates a Cartesian shape field.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="geometry">A supported built-in Cartesian geometry.</param>
    /// <param name="boost">The index-time field boost.</param>
    /// <param name="storeDocValues">Whether to retain Shape DocValues for spatial aggregations.</param>
    public XYShapeField(string name, IXYGeometry geometry, float boost = 1.0f, bool storeDocValues = true)
    {
        Name = FieldNameValidator.Validate(name, nameof(name));
        ArgumentNullException.ThrowIfNull(geometry);
        ShapeTessellator.ValidateXYFieldGeometry(geometry);
        Geometry = geometry;
        Boost = FieldBoostValidator.Validate(boost, nameof(boost));
        StoreDocValues = storeDocValues;
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
    public bool StoreDocValues { get; }

    /// <inheritdoc/>
    public FieldIndexOptions IndexOptions => FieldIndexOptions.DocsOnly;

    /// <inheritdoc/>
    public FieldType FieldType => FieldType.Binary;
}

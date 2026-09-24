using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>An indexed Cartesian point with coordinates measured in application-defined units.</summary>
public sealed class XYPointField : IField
{
    /// <summary>Creates an indexed Cartesian point.</summary>
    /// <param name="name">The point field name.</param>
    /// <param name="x">The finite x coordinate.</param>
    /// <param name="y">The finite y coordinate.</param>
    /// <param name="boost">The index-time field boost.</param>
    public XYPointField(string name, float x, float y, float boost = 1.0f)
    {
        var point = new XYPoint(x, y);
        Name = FieldNameValidator.Validate(name, nameof(name));
        X = point.X;
        Y = point.Y;
        Boost = FieldBoostValidator.Validate(boost, nameof(boost));
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Gets the finite x coordinate.</summary>
    public float X { get; }

    /// <summary>Gets the finite y coordinate.</summary>
    public float Y { get; }

    /// <inheritdoc/>
    /// <remarks>The existing binary category prevents generic LINQ translation from treating this point pair as one scalar number.</remarks>
    public FieldType FieldType => FieldType.Binary;

    /// <inheritdoc/>
    public bool IsStored => false;

    /// <inheritdoc/>
    public bool IsIndexed => true;

    /// <inheritdoc/>
    public float Boost { get; }

    /// <inheritdoc/>
    public bool StoreDocValues => true;

    /// <inheritdoc/>
    public FieldIndexOptions IndexOptions => FieldIndexOptions.DocsOnly;
}

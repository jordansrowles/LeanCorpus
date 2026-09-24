using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Search.XY;

/// <summary>Matches documents by a document-level relationship to a Cartesian shape.</summary>
/// <remarks>
/// Shape queries are constant-score queries. Boundaries are inclusive. A missing field matches no
/// relation, including <see cref="SpatialRelation.Disjoint"/>. A geometry collection is one union query.
/// </remarks>
public sealed class XYShapeQuery : Query
{
    /// <summary>Creates a Cartesian shape query.</summary>
    /// <param name="field">The indexed Cartesian shape field.</param>
    /// <param name="relation">The required document-level relationship.</param>
    /// <param name="geometry">A supported built-in query geometry, including query-only circles.</param>
    public XYShapeQuery(string field, SpatialRelation relation, IXYGeometry geometry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(geometry);
        if (!Enum.IsDefined(relation))
            throw new ArgumentOutOfRangeException(nameof(relation));
        ShapeTessellator.ValidateXYQueryGeometry(geometry);
        Field = field;
        Relation = relation;
        Geometry = geometry;
    }

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the requested spatial relation.</summary>
    public SpatialRelation Relation { get; }

    /// <summary>Gets the Cartesian query geometry.</summary>
    public IXYGeometry Geometry { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is XYShapeQuery other
            && Field == other.Field
            && Relation == other.Relation
            && Geometry.Equals(other.Geometry)
            && Boost.Equals(other.Boost);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(nameof(XYShapeQuery), Field, Relation, Geometry, Boost);
}

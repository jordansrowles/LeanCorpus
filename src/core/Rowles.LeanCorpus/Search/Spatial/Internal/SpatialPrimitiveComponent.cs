using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;


internal abstract class SpatialPrimitiveComponent : ISpatialComponent2D
{
    private readonly IReadOnlyList<ShapePrimitive> _primitives;
    private readonly bool _isGeo;
    private readonly SpatialEnvelope[] _rectangles;
    private readonly SpatialEnvelope[] _envelopes;
    private readonly bool _hasExactRectangleBounds;

    protected SpatialPrimitiveComponent(
        IReadOnlyList<ShapePrimitive> primitives,
        bool isGeo,
        SpatialEnvelope[]? rectangles = null)
    {
        _primitives = primitives;
        _isGeo = isGeo;
        _hasExactRectangleBounds = rectangles is not null;
        _rectangles = rectangles ?? [];
        SpatialEnvelope[] envelopes = _rectangles.Length == 0
            ? primitives.Select(SpatialComponentBounds.ForPrimitive).ToArray()
            : _rectangles;
        _envelopes = isGeo ? SpatialComponentBounds.AddGeoSeamAliases(envelopes) : envelopes;
    }

    public SpatialEnvelopeRelation RelateEnvelope(SpatialEnvelope envelope)
    {
        bool intersects = false;
        foreach (SpatialEnvelope componentEnvelope in _envelopes)
        {
            if (_hasExactRectangleBounds && componentEnvelope.Contains(envelope))
                return SpatialEnvelopeRelation.Inside;
            intersects |= componentEnvelope.Intersects(envelope);
        }
        return intersects ? SpatialEnvelopeRelation.Candidate : SpatialEnvelopeRelation.Outside;
    }

    public bool ContainsPoint(ShapeVertex point)
        => SpatialGeometryRelations.ContainsPoint(_primitives, point);

    public bool Intersects(ShapePrimitive primitive)
    {
        foreach (ShapePrimitive queryPrimitive in _primitives)
            if (SpatialGeometryRelations.Intersects(primitive, queryPrimitive, _isGeo))
                return true;
        return false;
    }

    public SpatialWithinRelation WithinRelation(IReadOnlyList<ShapePrimitive> indexedValue)
    {
        bool allCovered = true;
        bool intersects = false;
        foreach (ShapePrimitive queryPrimitive in _primitives)
        {
            if (PrimitiveCoveredByValue(queryPrimitive, indexedValue, _isGeo))
                intersects |= IntersectsValue(queryPrimitive, indexedValue, _isGeo);
            else
            {
                allCovered = false;
                intersects |= IntersectsValue(queryPrimitive, indexedValue, _isGeo);
            }
        }
        if (allCovered)
            return SpatialWithinRelation.Candidate;
        return intersects ? SpatialWithinRelation.NotWithin : SpatialWithinRelation.Disjoint;
    }

    protected static bool PrimitiveCoveredByValue(
        ShapePrimitive queryPrimitive,
        IReadOnlyList<ShapePrimitive> indexedValue,
        bool isGeo)
        => queryPrimitive.Kind switch
        {
            ShapePrimitiveKind.Point => SpatialGeometryRelations.ContainsPoint(indexedValue, queryPrimitive.A),
            ShapePrimitiveKind.Line => SpatialGeometryRelations.LineCoveredByPrimitives(
                queryPrimitive.A, queryPrimitive.B, indexedValue, isGeo),
            ShapePrimitiveKind.Triangle => SpatialGeometryRelations.TriangleCoveredByPrimitives(
                queryPrimitive, indexedValue, isGeo),
            _ => false,
        };

    protected static bool IntersectsValue(
        ShapePrimitive queryPrimitive,
        IReadOnlyList<ShapePrimitive> indexedValue,
        bool isGeo)
    {
        foreach (ShapePrimitive indexedPrimitive in indexedValue)
            if (SpatialGeometryRelations.Intersects(queryPrimitive, indexedPrimitive, isGeo))
                return true;
        return false;
    }
}

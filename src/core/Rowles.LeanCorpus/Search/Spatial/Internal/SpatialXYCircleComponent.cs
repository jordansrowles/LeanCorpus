using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;


internal sealed class SpatialXYCircleComponent : SpatialCircleComponent
{
    private readonly XYCircle _circle;

    internal SpatialXYCircleComponent(XYCircle circle, SpatialEnvelope[] envelopes)
        : base(envelopes, isGeo: false) => _circle = circle;

    public override bool ContainsPoint(ShapeVertex point)
        => SpatialGeometryRelations.ContainsPoint(_circle, point);

    public override bool Intersects(ShapePrimitive primitive)
        => SpatialGeometryRelations.IntersectsCircle(primitive, _circle);

    public override SpatialWithinRelation WithinRelation(IReadOnlyList<ShapePrimitive> indexedValue)
    {
        if (SpatialGeometryRelations.XYCircleCoveredByPrimitives(_circle, indexedValue))
            return SpatialWithinRelation.Candidate;
        foreach (ShapePrimitive primitive in indexedValue)
            if (SpatialGeometryRelations.IntersectsCircle(primitive, _circle))
                return SpatialWithinRelation.NotWithin;
        return SpatialWithinRelation.Disjoint;
    }
}

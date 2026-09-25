using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;


internal sealed class SpatialGeoCircleComponent : SpatialCircleComponent
{
    private readonly GeoCircle _circle;

    internal SpatialGeoCircleComponent(GeoCircle circle, SpatialEnvelope[] envelopes)
        : base(envelopes, isGeo: true) => _circle = circle;

    public override bool ContainsPoint(ShapeVertex point)
        => SpatialGeometryRelations.ContainsPoint(_circle, point);

    public override bool Intersects(ShapePrimitive primitive)
        => SpatialGeometryRelations.IntersectsCircle(primitive, _circle);

    public override SpatialWithinRelation WithinRelation(IReadOnlyList<ShapePrimitive> indexedValue)
    {
        if (SpatialGeometryRelations.GeoCircleCoveredByPrimitives(_circle, indexedValue))
            return SpatialWithinRelation.Candidate;
        foreach (ShapePrimitive primitive in indexedValue)
            if (SpatialGeometryRelations.IntersectsCircle(primitive, _circle))
                return SpatialWithinRelation.NotWithin;
        return SpatialWithinRelation.Disjoint;
    }
}

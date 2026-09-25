using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;


internal abstract class SpatialCircleComponent : ISpatialComponent2D
{
    private readonly SpatialEnvelope[] _envelopes;

    protected SpatialCircleComponent(SpatialEnvelope[] envelopes, bool isGeo)
        => _envelopes = isGeo ? SpatialComponentBounds.AddGeoSeamAliases(envelopes) : envelopes;

    public SpatialEnvelopeRelation RelateEnvelope(SpatialEnvelope envelope)
    {
        foreach (SpatialEnvelope circleEnvelope in _envelopes)
            if (circleEnvelope.Intersects(envelope))
                return SpatialEnvelopeRelation.Candidate;
        return SpatialEnvelopeRelation.Outside;
    }

    public abstract bool ContainsPoint(ShapeVertex point);

    public abstract bool Intersects(ShapePrimitive primitive);

    public abstract SpatialWithinRelation WithinRelation(IReadOnlyList<ShapePrimitive> indexedValue);
}

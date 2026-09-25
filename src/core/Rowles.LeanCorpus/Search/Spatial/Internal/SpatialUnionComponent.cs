using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;


internal sealed class SpatialUnionComponent : ISpatialComponent2D
{
    private readonly ISpatialComponent2D[] _components;

    internal SpatialUnionComponent(ISpatialComponent2D[] components) => _components = components;

    public SpatialEnvelopeRelation RelateEnvelope(SpatialEnvelope envelope)
    {
        bool candidate = false;
        foreach (ISpatialComponent2D component in _components)
        {
            SpatialEnvelopeRelation relation = component.RelateEnvelope(envelope);
            if (relation == SpatialEnvelopeRelation.Inside)
                return SpatialEnvelopeRelation.Inside;
            candidate |= relation == SpatialEnvelopeRelation.Candidate;
        }
        return candidate ? SpatialEnvelopeRelation.Candidate : SpatialEnvelopeRelation.Outside;
    }

    public bool ContainsPoint(ShapeVertex point)
    {
        foreach (ISpatialComponent2D component in _components)
            if (component.ContainsPoint(point))
                return true;
        return false;
    }

    public bool Intersects(ShapePrimitive primitive)
    {
        foreach (ISpatialComponent2D component in _components)
            if (component.Intersects(primitive))
                return true;
        return false;
    }

    public SpatialWithinRelation WithinRelation(IReadOnlyList<ShapePrimitive> indexedValue)
    {
        bool candidate = false;
        bool disjoint = false;
        foreach (ISpatialComponent2D component in _components)
        {
            switch (component.WithinRelation(indexedValue))
            {
                case SpatialWithinRelation.NotWithin:
                    return SpatialWithinRelation.NotWithin;
                case SpatialWithinRelation.Candidate:
                    candidate = true;
                    break;
                case SpatialWithinRelation.Disjoint:
                    disjoint = true;
                    break;
                default:
                    throw new InvalidDataException("A spatial component returned an unknown within relation.");
            }
        }

        if (candidate && disjoint)
            return SpatialWithinRelation.NotWithin;
        return candidate ? SpatialWithinRelation.Candidate : SpatialWithinRelation.Disjoint;
    }
}

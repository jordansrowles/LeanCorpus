using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;

/// <summary>A union tree over prepared query components. Nodes are allocated once per query, not per hit.</summary>
internal sealed class SpatialComponentTree : ISpatialComponent2D
{
    private readonly ISpatialComponent2D _root;

    internal SpatialComponentTree(IEnumerable<ISpatialComponent2D> components)
        => _root = new SpatialUnionComponent(components.ToArray());

    public SpatialEnvelopeRelation RelateEnvelope(SpatialEnvelope envelope)
        => _root.RelateEnvelope(envelope);

    public bool ContainsPoint(ShapeVertex point)
        => _root.ContainsPoint(point);

    public bool Intersects(ShapePrimitive primitive)
        => _root.Intersects(primitive);

    public SpatialWithinRelation WithinRelation(IReadOnlyList<ShapePrimitive> indexedValue)
        => _root.WithinRelation(indexedValue);
}

namespace Rowles.LeanCorpus.Search.Spatial.Internal;

internal enum SpatialEnvelopeRelation : byte
{
    Outside,
    Candidate,
    Inside,
}

/// <summary>Query-local spatial component predicates used by shape traversal and value containment.</summary>
internal interface ISpatialComponent2D
{
    SpatialEnvelopeRelation RelateEnvelope(SpatialEnvelope envelope);

    bool ContainsPoint(ShapeVertex point);

    bool Intersects(ShapePrimitive primitive);

    SpatialWithinRelation WithinRelation(IReadOnlyList<ShapePrimitive> indexedValue);
}

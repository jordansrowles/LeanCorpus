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

internal sealed class SpatialPointComponent : SpatialPrimitiveComponent
{
    internal SpatialPointComponent(IReadOnlyList<ShapePrimitive> primitives, bool isGeo)
        : base(primitives, isGeo) { }
}

internal sealed class SpatialLineComponent : SpatialPrimitiveComponent
{
    internal SpatialLineComponent(IReadOnlyList<ShapePrimitive> primitives, bool isGeo)
        : base(primitives, isGeo) { }
}

internal sealed class SpatialRectangleComponent : SpatialPrimitiveComponent
{
    internal SpatialRectangleComponent(
        IReadOnlyList<ShapePrimitive> primitives,
        bool isGeo,
        SpatialEnvelope[] rectangles)
        : base(primitives, isGeo, rectangles) { }
}

internal sealed class SpatialPolygonComponent : SpatialPrimitiveComponent
{
    internal SpatialPolygonComponent(IReadOnlyList<ShapePrimitive> primitives, bool isGeo)
        : base(primitives, isGeo) { }
}

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

internal static class SpatialComponentBounds
{
    internal static SpatialEnvelope[] AddGeoSeamAliases(SpatialEnvelope[] envelopes)
    {
        var aliases = new List<SpatialEnvelope>(envelopes.Length * 2);
        aliases.AddRange(envelopes);
        foreach (SpatialEnvelope envelope in envelopes)
        {
            if (envelope.MaxX == 180)
                aliases.Add(new SpatialEnvelope(envelope.MinX - 360, envelope.MinY, -180, envelope.MaxY));
            if (envelope.MinX == -180)
                aliases.Add(new SpatialEnvelope(180, envelope.MinY, envelope.MaxX + 360, envelope.MaxY));
        }
        return aliases.ToArray();
    }

    internal static SpatialEnvelope ForPrimitive(ShapePrimitive primitive)
        => new(
            Math.Min(primitive.A.X, Math.Min(primitive.B.X, primitive.C.X)),
            Math.Min(primitive.A.Y, Math.Min(primitive.B.Y, primitive.C.Y)),
            Math.Max(primitive.A.X, Math.Max(primitive.B.X, primitive.C.X)),
            Math.Max(primitive.A.Y, Math.Max(primitive.B.Y, primitive.C.Y)));
}

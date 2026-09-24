using Rowles.LeanCorpus.Index.Segment;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;

/// <summary>A coordinate pair in the canonical encoded spatial domain.</summary>
internal readonly record struct ShapeVertex
{
    internal ShapeVertex(double x, double y)
    {
        X = x;
        Y = y;
        XKey = 0;
        YKey = 0;
        PreparedKind = 0;
        HasPreparedKeys = false;
    }

    internal ShapeVertex(double x, double y, uint xKey, uint yKey, SpatialFieldKind preparedKind)
    {
        X = x;
        Y = y;
        XKey = xKey;
        YKey = yKey;
        PreparedKind = preparedKind;
        HasPreparedKeys = true;
    }

    internal double X { get; }
    internal double Y { get; }
    internal uint XKey { get; }
    internal uint YKey { get; }
    internal SpatialFieldKind PreparedKind { get; }
    internal bool HasPreparedKeys { get; }
}

/// <summary>The logical degeneracy represented by a shape primitive.</summary>
internal enum ShapePrimitiveKind : byte
{
    Point,
    Line,
    Triangle,
}

/// <summary>A decoded primitive backed entirely by value data.</summary>
internal readonly struct ShapePrimitive(
    ShapeVertex a,
    ShapeVertex b,
    ShapeVertex c,
    bool edgeAB,
    bool edgeBC,
    bool edgeCA,
    uint valueOrdinal,
    ShapePrimitiveKind kind)
{
    internal ShapeVertex A { get; } = a;
    internal ShapeVertex B { get; } = b;
    internal ShapeVertex C { get; } = c;
    internal bool EdgeAB { get; } = edgeAB;
    internal bool EdgeBC { get; } = edgeBC;
    internal bool EdgeCA { get; } = edgeCA;
    internal uint ValueOrdinal { get; } = valueOrdinal;
    internal ShapePrimitiveKind Kind { get; } = kind;
}

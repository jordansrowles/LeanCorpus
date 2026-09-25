using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;


internal sealed class SpatialRectangleComponent : SpatialPrimitiveComponent
{
    internal SpatialRectangleComponent(
        IReadOnlyList<ShapePrimitive> primitives,
        bool isGeo,
        SpatialEnvelope[] rectangles)
        : base(primitives, isGeo, rectangles) { }
}

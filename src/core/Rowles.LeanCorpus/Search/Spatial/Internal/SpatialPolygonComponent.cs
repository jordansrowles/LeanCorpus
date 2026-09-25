using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;


internal sealed class SpatialPolygonComponent : SpatialPrimitiveComponent
{
    internal SpatialPolygonComponent(IReadOnlyList<ShapePrimitive> primitives, bool isGeo)
        : base(primitives, isGeo) { }
}

using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;


internal sealed class SpatialPointComponent : SpatialPrimitiveComponent
{
    internal SpatialPointComponent(IReadOnlyList<ShapePrimitive> primitives, bool isGeo)
        : base(primitives, isGeo) { }
}

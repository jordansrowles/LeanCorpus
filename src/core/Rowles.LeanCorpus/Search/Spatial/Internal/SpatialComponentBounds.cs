using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;


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

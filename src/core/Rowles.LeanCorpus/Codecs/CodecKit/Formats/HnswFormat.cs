using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// HNSW graph (.hnsw) wire format (v1).
/// Layout: [dimension:Int32LE][normalised:UInt8][M:Int32LE][M0:Int32LE][efConstruction:Int32LE]
///   [seed:Int64LE][entryPoint:Int32LE][maxLevel:Int32LE][nodeCount:Int32LE][levelCount:Int32LE]
///   per level (descending): [nodeCount:Int32LE] per node: [docId:Int32LE][neighbourCount:Int32LE][neighbours:Int32LE*neighbourCount]
/// </summary>
internal static class HnswFormat
{
    internal sealed class Data
    {
        public int Dimension { get; init; }
        public bool Normalised { get; init; }
        public IReadOnlyList<byte> Body { get; init; } = [];
    }

    internal static readonly ICodec<Data> V1 = Codec.Record<Data>()
        .Field("dimension",  d => d.Dimension,  Codec.Int32LE)
        .Field("normalised", d => d.Normalised, Codec.Bool)
        .Field("bodyLen",    d => d.Body.Count, Codec.Int32LE)
        .Field("body",       d => d.Body,        Codec.UInt8.RepeatFrom("bodyLen"))
        .Build<int, bool, int, IReadOnlyList<byte>>((dimension, normalised, bodyLen, body) =>
            new Data { Dimension = dimension, Normalised = normalised, Body = body });
}

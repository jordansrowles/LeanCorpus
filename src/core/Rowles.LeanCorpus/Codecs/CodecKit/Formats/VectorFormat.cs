using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// Dense float vector (.vec) wire format (per-field file).
/// Layout: [docCount:Int32LE][dimension:Int32LE][format:UInt8][Float32LE values *docCount*dimension]
/// </summary>
internal static class VectorFormat
{
    internal sealed class Data
    {
        public int DocCount { get; init; }
        public int Dimension { get; init; }
        public byte Format { get; init; }
        public float[] Values { get; init; } = [];
    }

    // File-level codec: docCount, dimension, format, then flat float values
    internal static readonly ICodec<Data> V1 = Codec.Record<Data>()
        .Field("docCount",  d => d.DocCount,  Codec.Int32LE)
        .Field("dimension", d => d.Dimension, Codec.Int32LE)
        .Field("format",    d => d.Format,    Codec.UInt8)
        .Field("valueCount", d => d.Values.Length, Codec.Int32LE)
        .Field("values",    d => d.Values,    Codec.Float32LE.RepeatFrom("valueCount"))
        .Build<int, int, byte, int, float[]>((docCount, dimension, format, valueCount, values) =>
            new Data { DocCount = docCount, Dimension = dimension, Format = format, Values = values });
}

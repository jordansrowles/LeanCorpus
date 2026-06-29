using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// Quantised vector (.qvec) wire format (per-field file).
/// Layout: [docCount:Int32LE][dimension:Int32LE][quantisation:UInt8]
/// then quantisation-specific data followed by [packed bytes].
/// </summary>
internal static class QuantisedVectorFormat
{
    internal sealed class Data
    {
        public int DocCount { get; init; }
        public int Dimension { get; init; }
        public byte Quantisation { get; init; }
        public float QMin { get; init; }
        public float QAlpha { get; init; }
        public int TailLength { get; init; }
        public IReadOnlyList<byte> Tail { get; init; } = [];
    }

    // File-level codec: header fields then opaque tail
    internal static readonly ICodec<Data> V1 = Codec.Record<Data>()
        .Field("docCount",     d => d.DocCount,     Codec.Int32LE)
        .Field("dimension",    d => d.Dimension,    Codec.Int32LE)
        .Field("quantisation", d => d.Quantisation, Codec.UInt8)
        .Field("qMin",         d => d.QMin,         Codec.Float32LE)
        .Field("qAlpha",       d => d.QAlpha,       Codec.Float32LE)
        .Field("tailLength",   d => d.Tail.Count,   Codec.Int32LE)
        .Field("tail",         d => d.Tail,         Codec.UInt8.RepeatFrom("tailLength"))
        .Build<int, int, byte, float, float, int, IReadOnlyList<byte>>(
            (docCount, dimension, quantisation, qMin, qAlpha, tailLength, tail) =>
                new Data { DocCount = docCount, Dimension = dimension, Quantisation = quantisation, QMin = qMin, QAlpha = qAlpha, TailLength = tailLength, Tail = tail });
}

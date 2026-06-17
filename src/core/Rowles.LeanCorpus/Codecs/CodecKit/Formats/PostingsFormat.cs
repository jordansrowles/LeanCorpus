using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// Postings (.pos) wire format (v1 block postings).
/// Per-term layout: [docFreq:Int32LE][skipOffset:Int64LE][hasFreqs:Boolean][hasPositions:Boolean][hasPayloads:Boolean]
/// then blocks of packed doc-deltas and frequencies, followed by position data and skip index.
/// </summary>
internal static class PostingsFormat
{
    internal sealed class Data
    {
        public int DocFreq { get; init; }
        public long SkipOffset { get; init; }
        public bool HasFreqs { get; init; }
        public bool HasPositions { get; init; }
        public bool HasPayloads { get; init; }
        public IReadOnlyList<byte> Body { get; init; } = [];
    }

    internal static readonly ICodec<Data> V1 = Codec.Record<Data>()
        .Field("docFreq",      d => d.DocFreq,      Codec.Int32LE)
        .Field("skipOffset",   d => d.SkipOffset,   Codec.Int64LE)
        .Field("hasFreqs",     d => d.HasFreqs,     Codec.Bool)
        .Field("hasPositions", d => d.HasPositions, Codec.Bool)
        .Field("hasPayloads",  d => d.HasPayloads,  Codec.Bool)
        .Field("bodyLen",      d => d.Body.Count,  Codec.Int32LE)
        .Field("body",         d => d.Body,         Codec.UInt8.RepeatFrom("bodyLen"))
        .Build<int, long, bool, bool, bool, int, IReadOnlyList<byte>>(
            (docFreq, skipOffset, hasFreqs, hasPositions, hasPayloads, bodyLen, body) =>
                new Data { DocFreq = docFreq, SkipOffset = skipOffset, HasFreqs = hasFreqs, HasPositions = hasPositions, HasPayloads = hasPayloads, Body = body });
}

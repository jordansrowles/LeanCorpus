using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// Term vectors (.tvx/.tvd) wire format (v2).
/// Layout (.tvx index): [docCount:Int32LE][offsets:Int64LE*docCount]
/// Layout (.tvd data): per doc: [fieldCount:Int32LE] per field: [name:string][termCount:Int32LE]
///   per term: [term:string][freq:Int32LE][posCount:Int32LE][positions:Int32LE*posCount][hasPayloads:Boolean][payloads]
/// </summary>
internal static class TermVectorsFormat
{
    internal sealed class Data
    {
        public int DocCount { get; init; }
        public IReadOnlyList<byte> Body { get; init; } = [];
    }

    internal static readonly ICodec<Data> V2 = Codec.Record<Data>()
        .Field("docCount", d => d.DocCount, Codec.Int32LE)
        .Field("bodyLen",  d => d.Body.Count, Codec.Int32LE)
        .Field("body",     d => d.Body, Codec.UInt8.RepeatFrom("bodyLen"))
        .Build<int, int, IReadOnlyList<byte>>((docCount, bodyLen, body) =>
            new Data { DocCount = docCount, Body = body });
}

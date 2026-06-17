using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// Term dictionary (.tim/.dic) wire format (v1 FST).
/// Layout: [FST blob bytes] — the FST blob is self-describing (starts with its own "FST1" magic).
/// </summary>
internal static class TermDictionaryFormat
{
    internal sealed class Data
    {
        public int FstLength { get; init; }
        public IReadOnlyList<byte> FstBlob { get; init; } = [];
    }

    internal static readonly ICodec<Data> V1 = Codec.Record<Data>()
        .Field("fstLength", d => d.FstBlob.Count, Codec.Int32LE)
        .Field("fstBlob",   d => d.FstBlob,   Codec.UInt8.RepeatFrom("fstLength"))
        .Build<int, IReadOnlyList<byte>>((fstLength, fstBlob) =>
            new Data { FstLength = fstBlob.Count, FstBlob = fstBlob });
}

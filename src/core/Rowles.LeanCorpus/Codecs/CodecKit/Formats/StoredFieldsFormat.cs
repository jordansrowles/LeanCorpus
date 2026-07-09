using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// Legacy stored fields body codec used by the v1 CodecKit envelope.
/// Current writers stream the v2 format directly via <see cref="StoredFieldsFileHeader"/>
/// and no longer buffer the entire body.
/// Layout (v1 envelope body): [blockSize:Int32LE][compression:UInt8][blocksLen:Int32LE][blocks:UInt8*]
/// where each block is [docCount:Int32LE][rawLen:Int32LE][compLen:Int32LE][intraOffsets:Int32LE*docCount][compressed bytes].
/// </summary>
internal static class StoredFieldsFormat
{
    internal sealed class Data
    {
        public int BlockSize { get; init; }
        public byte Compression { get; init; }
        public IReadOnlyList<byte> Blocks { get; init; } = [];
    }

    internal static readonly ICodec<Data> V1 = Codec.Record<Data>()
        .Field("blockSize",   d => d.BlockSize,   Codec.Int32LE)
        .Field("compression", d => d.Compression, Codec.UInt8)
        .Field("blocksLen",   d => d.Blocks.Count, Codec.Int32LE)
        .Field("blocks",      d => d.Blocks,      Codec.UInt8.RepeatFrom("blocksLen"))
        .Build<int, byte, int, IReadOnlyList<byte>>((blockSize, compression, blocksLen, blocks) =>
            new Data { BlockSize = blockSize, Compression = compression, Blocks = blocks });
}

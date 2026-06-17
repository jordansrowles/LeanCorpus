using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// Stored fields data (.fdt) wire format.
/// Layout: [blockSize:Int32LE][compression:UInt8]
/// then repeated blocks: [docCount:Int32LE][rawLen:Int32LE][compLen:Int32LE][intraOffsets:Int32LE*docCount][compressed bytes].
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

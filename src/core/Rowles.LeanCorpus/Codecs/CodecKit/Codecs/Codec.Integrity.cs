using Rowles.LeanCorpus.Codecs.CodecKit.Internal;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Compression;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

public static partial class Codec
{
    /// <summary>
    /// Wraps a codec with checksum computation and verification.
    /// </summary>
    public static ICodec<T> WithChecksum<T>(this ICodec<T> codec, ChecksumAlgorithmId algorithmId,
        ChecksumPlacement placement)
        => new WithChecksumCodec<T>(algorithmId, placement, codec);

    /// <summary>
    /// Wraps a codec with Deflate compression/decompression.
    /// Wire format: [compressed-length][compressed-payload].
    /// </summary>
    public static ICodec<T> WithCompression<T>(this ICodec<T> codec,
        CodecCompressionLevel level = CodecCompressionLevel.Optimal, ICodec<uint>? compressedLengthCodec = null)
    {
        ICodec<long> lengthCodec = compressedLengthCodec != null
            ? new NumericToLongCodec<uint>(compressedLengthCodec, v => (long)v, v => checked((uint)v))
            : new NumericToLongCodec<uint>(VarUInt32, v => (long)v, v => checked((uint)v));
        return new WithCompressionCodec<T>(level, lengthCodec, codec);
    }
}

using System;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

public static partial class Codec
{
    /// <summary>
    /// Length-prefixed framing: <c>[length][body]</c>.
    /// The length codec writes/reads the body byte count.
    /// </summary>
    public static ICodec<T> LengthPrefixed<T>(ICodec<int> lengthCodec, ICodec<T> innerCodec,
        TrailingDataPolicy trailingData = TrailingDataPolicy.Reject)
        => new LengthPrefixedCodec<T>(new NumericToLongCodec<int>(lengthCodec, v => v, v => (int)v), innerCodec, trailingData);

    /// <summary>
    /// Length-prefixed framing with a uint length codec.
    /// </summary>
    public static ICodec<T> LengthPrefixed<T>(ICodec<uint> lengthCodec, ICodec<T> innerCodec,
        TrailingDataPolicy trailingData = TrailingDataPolicy.Reject)
        => new LengthPrefixedCodec<T>(new NumericToLongCodec<uint>(lengthCodec, v => v, v => (uint)v), innerCodec, trailingData);

    /// <summary>
    /// Length-prefixed framing with a long length codec.
    /// </summary>
    public static ICodec<T> LengthPrefixed<T>(ICodec<long> lengthCodec, ICodec<T> innerCodec,
        TrailingDataPolicy trailingData = TrailingDataPolicy.Reject)
        => new LengthPrefixedCodec<T>(new NumericToLongCodec<long>(lengthCodec, v => v, v => v), innerCodec, trailingData);

    /// <summary>
    /// Fixed-frame framing: <c>[body][padding]</c> with exactly <paramref name="size"/> total bytes.
    /// </summary>
    public static ICodec<T> FixedFrame<T>(int size, ICodec<T> innerCodec, FramePadding? padding = null,
        TrailingDataPolicy trailingData = TrailingDataPolicy.Reject)
        => new FixedFrameCodec<T>(size, innerCodec, padding ?? FramePadding.ZeroFill, trailingData);
}

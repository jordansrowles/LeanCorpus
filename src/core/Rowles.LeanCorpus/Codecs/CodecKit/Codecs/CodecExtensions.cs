using Rowles.LeanCorpus.Codecs.CodecKit;
using System;
using System.Buffers;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

/// <summary>
/// Extension methods for convenient codec usage.
/// </summary>
internal static class CodecExtensions
{
    /// <summary>Decodes a value from a byte array.</summary>
    public static T Decode<T>(this ICodec<T> codec, byte[] data, CodecOptions? options = null, CodecRegistry? registry = null)
        => Codec.Decode(codec, data, options, registry);

    /// <summary>Decodes a value from a ReadOnlySequence.</summary>
    public static T Decode<T>(this ICodec<T> codec, ReadOnlySequence<byte> data, CodecOptions? options = null, CodecRegistry? registry = null)
        => Codec.Decode(codec, data, options, registry);

    /// <summary>Encodes a value to a new byte array.</summary>
    public static byte[] EncodeToArray<T>(this ICodec<T> codec, T value, CodecOptions? options = null, CodecRegistry? registry = null)
        => Codec.EncodeToArray(codec, value, options, registry);
}

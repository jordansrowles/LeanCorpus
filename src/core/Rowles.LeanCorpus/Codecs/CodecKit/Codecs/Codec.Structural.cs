using System;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

public static partial class Codec
{
    /// <summary>
    /// Creates a codec that transforms values using the given encode/decode delegates.
    /// User delegate exceptions are wrapped in <see cref="Rowles.LeanCorpus.Codecs.CodecKit.Exceptions.UserCodeException"/>.
    /// </summary>
    public static ICodec<TOut> Map<TIn, TOut>(this ICodec<TIn> codec, Func<TIn, TOut> decode, Func<TOut, TIn> encode)
        => new MapCodec<TIn, TOut>(codec, decode, encode);

    /// <summary>
    /// Sequences two codecs: first is decoded/encoded but its result is discarded.
    /// The returned codec operates on the second codec's type.
    /// </summary>
    public static ICodec<TSecond> Then<TFirst, TSecond>(this ICodec<TFirst> first, ICodec<TSecond> second)
        => new ThenCodec<TFirst, TSecond>(first, second);

    /// <summary>
    /// Wraps a codec with a validation predicate. On decode or encode, if the predicate
    /// returns false, a <see cref="Exceptions.CodecValidationException"/> is thrown.
    /// </summary>
    public static ICodec<T> Validate<T>(this ICodec<T> codec, Func<T, bool> predicate, Func<T, string> message)
        => new ValidateCodec<T>(codec, predicate, message);

    /// <summary>
    /// Wraps a codec with a validation predicate and a fixed error message.
    /// </summary>
    public static ICodec<T> Validate<T>(this ICodec<T> codec, Func<T, bool> predicate, string message)
        => new ValidateCodec<T>(codec, predicate, _ => message);
}

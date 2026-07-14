using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>
/// Primary entry point for all codec operations. Provides factory methods for primitive
/// and composite codecs, as well as top-level encode/decode entry points.
/// </summary>
public static partial class Codec
{
    /// <summary>
    /// Decodes a value of type <typeparamref name="T"/> from the given bytes.
    /// Throws a <see cref="CodecException"/> on failure.
    /// </summary>
    public static T Decode<T>(ICodec<T> codec, ReadOnlySequence<byte> data, CodecOptions? options = null, CodecRegistry? registry = null)
    {
        var ctx = new CodecContext(options ?? CodecOptions.Default, registry ?? CodecRegistry.Default);
        var reader = new SequenceReader<byte>(data);
        return codec.Decode(ref reader, ctx);
    }

    /// <summary>
    /// Decodes a value from a byte array.
    /// </summary>
    public static T Decode<T>(ICodec<T> codec, byte[] data, CodecOptions? options = null, CodecRegistry? registry = null)
    {
        return Decode(codec, new ReadOnlySequence<byte>(data), options, registry);
    }

    /// <summary>
    /// Decodes a value from a ReadOnlySpan (copies to array first).
    /// </summary>
    public static T Decode<T>(ICodec<T> codec, ReadOnlySpan<byte> data, CodecOptions? options = null, CodecRegistry? registry = null)
    {
        return Decode(codec, data.ToArray(), options, registry);
    }

    /// <summary>
    /// Tries to decode a value. Returns a <see cref="CodecResult{T}"/> instead of throwing.
    /// On failure, the reader position is restored.
    /// </summary>
    public static CodecResult<T> TryDecode<T>(ICodec<T> codec, ReadOnlySequence<byte> data, CodecOptions? options = null, CodecRegistry? registry = null)
    {
        var ctx = new CodecContext(options ?? CodecOptions.Default, registry ?? CodecRegistry.Default);
        var reader = new SequenceReader<byte>(data);
        try
        {
            var value = codec.Decode(ref reader, ctx);
            return CodecResult<T>.Success(value);
        }
        catch (CodecException ex)
        {
            return CodecResult<T>.Fail(ex);
        }
    }

    /// <summary>
    /// Tries to decode a value from a byte array.
    /// </summary>
    public static CodecResult<T> TryDecode<T>(ICodec<T> codec, byte[] data, CodecOptions? options = null, CodecRegistry? registry = null)
    {
        return TryDecode(codec, new ReadOnlySequence<byte>(data), options, registry);
    }

    /// <summary>
    /// Encodes a value to the given writer.
    /// </summary>
    public static void Encode<T>(ICodec<T> codec, T value, IBufferWriter<byte> writer, CodecOptions? options = null, CodecRegistry? registry = null)
    {
        var ctx = new CodecContext(options ?? CodecOptions.Default, registry ?? CodecRegistry.Default);
        codec.Encode(value, writer, ctx);
    }

    /// <summary>
    /// Encodes a <see cref="ReadOnlySpan{Byte}"/> body through a version-envelope codec
    /// without allocating a <c>byte[]</c>. If the codec is a
    /// <see cref="VersionEnvelopeCodec{TBase,TVersion}"/> the fast span path is used;
    /// otherwise falls back to the normal <see cref="Encode{T}"/> path.
    /// </summary>
    public static void EncodeSpan(ICodec<byte[]> codec, ReadOnlySpan<byte> body, IBufferWriter<byte> writer, CodecOptions? options = null, CodecRegistry? registry = null)
    {
        var ctx = new CodecContext(options ?? CodecOptions.Default, registry ?? CodecRegistry.Default);

        if (codec is VersionEnvelopeCodec<byte[], byte> envelope)
        {
            envelope.EncodeSpan(body, writer, ctx);
        }
        else
        {
            codec.Encode(body.ToArray(), writer, ctx);
        }
    }

    /// <summary>
    /// Encodes a value and returns the bytes as a new array.
    /// </summary>
    public static byte[] EncodeToArray<T>(ICodec<T> codec, T value, CodecOptions? options = null, CodecRegistry? registry = null)
    {
        var writer = new ArrayBufferWriter<byte>();
        Encode(codec, value, writer, options, registry);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Tries to encode a value. Returns a <see cref="CodecResult{Unit}"/> instead of throwing.
    /// </summary>
    public static CodecResult<Unit> TryEncode<T>(ICodec<T> codec, T value, IBufferWriter<byte> writer, CodecOptions? options = null, CodecRegistry? registry = null)
    {
        var ctx = new CodecContext(options ?? CodecOptions.Default, registry ?? CodecRegistry.Default);
        try
        {
            codec.Encode(value, writer, ctx);
            return CodecResult<Unit>.Success(Unit.Value);
        }
        catch (CodecException ex)
        {
            return CodecResult<Unit>.Fail(ex);
        }
    }

    /// <summary>
    /// Encodes a value atomically — stages to a scratch buffer first, only commits to the writer on success.
    /// If encoding fails, the writer remains untouched.
    /// </summary>
    public static void EncodeAtomic<T>(ICodec<T> codec, T value, IBufferWriter<byte> writer, CodecOptions? options = null, CodecRegistry? registry = null)
    {
        var ctx = new CodecContext(options ?? CodecOptions.Default, registry ?? CodecRegistry.Default);
        using var scratch = ctx.RentScratchBuffer();
        codec.Encode(value, scratch, ctx);

        // Commit staged bytes to the real writer
        var written = scratch.Written;
        foreach (var segment in written)
        {
            var span = writer.GetSpan(segment.Length);
            segment.Span.CopyTo(span);
            writer.Advance(segment.Length);
        }
    }
}

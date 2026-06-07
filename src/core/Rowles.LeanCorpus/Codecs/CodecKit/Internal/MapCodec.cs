using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// Transforms values between two types using user-supplied delegates.
/// Wraps delegate exceptions in <see cref="UserCodeException"/>.
/// </summary>
internal sealed class MapCodec<TIn, TOut> : ICodec<TOut>
{
    private readonly ICodec<TIn> _inner;
    private readonly Func<TIn, TOut> _decode;
    private readonly Func<TOut, TIn> _encode;

    public MapCodec(ICodec<TIn> inner, Func<TIn, TOut> decode, Func<TOut, TIn> encode)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _decode = decode ?? throw new ArgumentNullException(nameof(decode));
        _encode = encode ?? throw new ArgumentNullException(nameof(encode));
    }

    public TOut Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        var checkpoint = context.Checkpoint(ref reader);
        TIn raw;
        try
        {
            using var depthGuard = context.PushDepth();
            raw = _inner.Decode(ref reader, context);
        }
        catch
        {
            context.Rewind(ref reader, checkpoint);
            throw;
        }

        try
        {
            return _decode(raw);
        }
        catch (CodecException)
        {
            context.Rewind(ref reader, checkpoint);
            throw;
        }
        catch (Exception ex)
        {
            context.Rewind(ref reader, checkpoint);
            throw new UserCodeException(context.GetByteOffset(ref reader), context.CurrentPath, ex);
        }
    }

    public void Encode(TOut value, IBufferWriter<byte> writer, CodecContext context)
    {
        TIn raw;
        try
        {
            raw = _encode(value);
        }
        catch (CodecException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UserCodeException(0, context.CurrentPath, ex);
        }

        using var depthGuard = context.PushDepth();
        _inner.Encode(raw, writer, context);
    }
}

using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using System;
using System.Buffers;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// Sequences two codecs: decodes first (discards result), then decodes second (returns result).
/// Encode is reversed: encodes first value (from encode delegate or default), then second.
/// </summary>
internal sealed class ThenCodec<TFirst, TSecond> : ICodec<TSecond>
{
    private readonly ICodec<TFirst> _first;
    private readonly ICodec<TSecond> _second;

    public ThenCodec(ICodec<TFirst> first, ICodec<TSecond> second)
    {
        _first = first ?? throw new ArgumentNullException(nameof(first));
        _second = second ?? throw new ArgumentNullException(nameof(second));
    }

    public TSecond Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        var checkpoint = context.Checkpoint(ref reader);
        try
        {
            using var depthGuard = context.PushDepth();
            _first.Decode(ref reader, context);
            return _second.Decode(ref reader, context);
        }
        catch
        {
            context.Rewind(ref reader, checkpoint);
            throw;
        }
    }

    public void Encode(TSecond value, IBufferWriter<byte> writer, CodecContext context)
    {
        using var depthGuard = context.PushDepth();
        _first.Encode(default!, writer, context);
        _second.Encode(value, writer, context);
    }
}

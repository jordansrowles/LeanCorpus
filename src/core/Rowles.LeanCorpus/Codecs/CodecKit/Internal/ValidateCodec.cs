using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// Validates a decoded value using a predicate. Throws <see cref="CodecValidationException"/>
/// if the predicate returns false.
/// </summary>
internal sealed class ValidateCodec<T> : ICodec<T>
{
    private readonly ICodec<T> _inner;
    private readonly Func<T, bool> _predicate;
    private readonly Func<T, string> _messageFactory;

    public ValidateCodec(ICodec<T> inner, Func<T, bool> predicate, Func<T, string> messageFactory)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _messageFactory = messageFactory ?? throw new ArgumentNullException(nameof(messageFactory));
    }

    public T Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        var checkpoint = context.Checkpoint(ref reader);
        T value;
        try
        {
            using var depthGuard = context.PushDepth();
            value = _inner.Decode(ref reader, context);
        }
        catch
        {
            context.Rewind(ref reader, checkpoint);
            throw;
        }

        using var _ = context.PushPath("{validate}");
        if (!_predicate(value))
        {
            context.Rewind(ref reader, checkpoint);
            throw new CodecValidationException(context.GetByteOffset(ref reader), context.CurrentPath, _messageFactory(value));
        }

        return value;
    }

    public void Encode(T value, IBufferWriter<byte> writer, CodecContext context)
    {
        if (!_predicate(value))
        {
            throw new CodecValidationException(0, context.CurrentPath, _messageFactory(value));
        }

        using var depthGuard = context.PushDepth();
        _inner.Encode(value, writer, context);
    }
}

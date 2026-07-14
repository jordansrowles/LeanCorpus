using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// Length-prefixed framing: [length][body].
/// Creates a delimited scope for the inner codec.
/// </summary>
internal sealed class LengthPrefixedCodec<T> : ICodec<T>
{
    private readonly ICodec<long> _lengthCodec;
    private readonly ICodec<T> _innerCodec;
    private readonly TrailingDataPolicy _trailingDataPolicy;

    public LengthPrefixedCodec(ICodec<long> lengthCodec, ICodec<T> innerCodec, TrailingDataPolicy trailingDataPolicy)
    {
        _lengthCodec = lengthCodec ?? throw new ArgumentNullException(nameof(lengthCodec));
        _innerCodec = innerCodec ?? throw new ArgumentNullException(nameof(innerCodec));
        _trailingDataPolicy = trailingDataPolicy;
    }

    public T Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        var checkpoint = context.Checkpoint(ref reader);

        long length;
        try
        {
            using var _ = context.PushPath("{framed}");
            length = _lengthCodec.Decode(ref reader, context);

            if (length < 0)
                throw new CodecValidationException(
                    CodecErrorCode.InvalidValue,
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    $"Negative frame length: {length}");

            if (length > int.MaxValue)
                throw new LimitExceededException(
                    CodecErrorCode.FrameTooLarge,
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    "FrameLength", length, int.MaxValue);

            if (length > context.Options.MaxFrameBytes)
                throw new LimitExceededException(
                    CodecErrorCode.FrameTooLarge,
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    "FrameLength", length, context.Options.MaxFrameBytes);

            int len = (int)length;
            if (reader.Remaining < len)
                throw new InsufficientDataException(
                    context.GetByteOffset(ref reader),
                    context.CurrentPath, len, (int)reader.Remaining);

            // Slice the body from the reader
            var bodySequence = reader.Sequence.Slice(reader.Position, len);
            var bodyReader = new SequenceReader<byte>(bodySequence);

            using var depthGuard = context.PushDepth();
            using var scope = context.EnterScope(len);
            T value = _innerCodec.Decode(ref bodyReader, context);

            // Enforce trailing data policy within the body scope
            if (bodyReader.Remaining > 0)
            {
                if (_trailingDataPolicy == TrailingDataPolicy.Reject)
                {
                    throw new TrailingDataException(
                        context.GetByteOffset(ref reader) + bodyReader.Consumed,
                        context.CurrentPath,
                        (int)bodyReader.Remaining);
                }
            }

            // Advance the outer reader past the entire body
            reader.Advance(len);

            // Enforce trailing data policy beyond the declared frame length
            if (_trailingDataPolicy == TrailingDataPolicy.Reject && reader.Remaining > 0)
            {
                throw new TrailingDataException(
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    (int)reader.Remaining);
            }

            return value;
        }
        catch
        {
            context.Rewind(ref reader, checkpoint);
            throw;
        }
    }

    public void Encode(T value, IBufferWriter<byte> writer, CodecContext context)
    {
        using var pathGuard = context.PushPath("{framed}");
        using var depthGuard = context.PushDepth();

        // Stage the inner payload to a scratch buffer to measure its length
        var scratch = context.RentScratchBuffer();
        try
        {
            _innerCodec.Encode(value, scratch, context);

            long payloadLength = scratch.Length;

            // Write the length prefix
            _lengthCodec.Encode(payloadLength, writer, context);

            // Write the staged payload
            var written = scratch.Written;
            foreach (var segment in written)
            {
                var span = writer.GetSpan(segment.Length);
                segment.Span.CopyTo(span);
                writer.Advance(segment.Length);
            }
        }
        finally
        {
            context.ReturnScratchBuffer(scratch);
        }
    }
}

/// <summary>
/// Adapter to convert any numeric codec to ICodec&lt;long&gt; for length prefix use.
/// </summary>
internal sealed class NumericToLongCodec<TNumeric> : ICodec<long> where TNumeric : struct
{
    private readonly ICodec<TNumeric> _inner;
    private readonly Func<TNumeric, long> _toLong;
    private readonly Func<long, TNumeric> _fromLong;

    public NumericToLongCodec(ICodec<TNumeric> inner, Func<TNumeric, long> toLong, Func<long, TNumeric> fromLong)
    {
        _inner = inner;
        _toLong = toLong;
        _fromLong = fromLong;
    }

    public long Decode(ref SequenceReader<byte> reader, CodecContext context)
        => _toLong(_inner.Decode(ref reader, context));

    public void Encode(long value, IBufferWriter<byte> writer, CodecContext context)
    {
        try
        {
            _inner.Encode(_fromLong(value), writer, context);
        }
        catch (OverflowException ex)
        {
            throw new LimitExceededException(
                CodecErrorCode.FrameTooLarge, 0, context.CurrentPath,
                "PayloadLength", value, typeof(TNumeric).Name + ".MaxValue", ex);
        }
    }
}

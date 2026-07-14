using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// Flag-based optional: [flag][inner?]. Absent = flag false, Present = flag true + body.
/// </summary>
internal sealed class OptionalCodec<T> : ICodec<T?>
{
    private readonly ICodec<T> _innerCodec;
    private readonly ICodec<bool> _flagCodec;

    public OptionalCodec(ICodec<T> innerCodec, ICodec<bool> flagCodec)
    {
        _innerCodec = innerCodec ?? throw new ArgumentNullException(nameof(innerCodec));
        _flagCodec = flagCodec ?? throw new ArgumentNullException(nameof(flagCodec));
    }

    public T? Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        var checkpoint = context.Checkpoint(ref reader);
        try
        {
            using var pathGuard = context.PushPath("{optional}");
            bool present = _flagCodec.Decode(ref reader, context);

            if (!present)
                return default;

            using var depthGuard = context.PushDepth();
            return _innerCodec.Decode(ref reader, context);
        }
        catch
        {
            context.Rewind(ref reader, checkpoint);
            throw;
        }
    }

    public void Encode(T? value, IBufferWriter<byte> writer, CodecContext context)
    {

        if (value is null)
        {
            _flagCodec.Encode(false, writer, context);
        }
        else
        {
            _flagCodec.Encode(true, writer, context);
            using var depthGuard = context.PushDepth();
            _innerCodec.Encode(value, writer, context);
        }
    }
}

/// <summary>
/// Sentinel-based optional: [sentinel][inner?]. Absent = absentValue, Present = presentValue + body.
/// </summary>
internal sealed class OptionalSentinelCodec<T, TSentinel> : ICodec<T?> where TSentinel : IEquatable<TSentinel>
{
    private readonly ICodec<T> _innerCodec;
    private readonly ICodec<TSentinel> _sentinelCodec;
    private readonly TSentinel _absentValue;
    private readonly TSentinel _presentValue;

    public OptionalSentinelCodec(ICodec<T> innerCodec, ICodec<TSentinel> sentinelCodec,
        TSentinel absentValue, TSentinel presentValue)
    {
        _innerCodec = innerCodec ?? throw new ArgumentNullException(nameof(innerCodec));
        _sentinelCodec = sentinelCodec ?? throw new ArgumentNullException(nameof(sentinelCodec));

        if (absentValue is null) throw new ArgumentNullException(nameof(absentValue));
        if (presentValue is null) throw new ArgumentNullException(nameof(presentValue));

        if (absentValue.Equals(presentValue))
            throw new ArgumentException("absentValue and presentValue must be different.");

        _absentValue = absentValue;
        _presentValue = presentValue;
    }

    public T? Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        var checkpoint = context.Checkpoint(ref reader);
        try
        {
            TSentinel sentinel = _sentinelCodec.Decode(ref reader, context);

            if (sentinel.Equals(_absentValue))
                return default;

            if (!sentinel.Equals(_presentValue))
            {
                throw new CodecValidationException(
                    CodecErrorCode.InvalidValue,
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    $"Unexpected sentinel value: expected {_absentValue} or {_presentValue}, got {sentinel}");
            }

            using var depthGuard = context.PushDepth();
            return _innerCodec.Decode(ref reader, context);
        }
        catch
        {
            context.Rewind(ref reader, checkpoint);
            throw;
        }
    }

    public void Encode(T? value, IBufferWriter<byte> writer, CodecContext context)
    {

        if (value is null)
        {
            _sentinelCodec.Encode(_absentValue, writer, context);
        }
        else
        {
            _sentinelCodec.Encode(_presentValue, writer, context);
            using var depthGuard = context.PushDepth();
            _innerCodec.Encode(value, writer, context);
        }
    }
}

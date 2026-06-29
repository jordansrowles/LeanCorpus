using System;
using System.Buffers;
using System.Collections.Generic;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// Decodes/encodes a fixed number of elements.
/// </summary>
internal sealed class RepeatCodec<T> : ICodec<IReadOnlyList<T>>
{
    private readonly ICodec<T> _elementCodec;
    private readonly int _count;

    public RepeatCodec(ICodec<T> elementCodec, int count)
    {
        _elementCodec = elementCodec ?? throw new ArgumentNullException(nameof(elementCodec));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Count must be non-negative.");
        _count = count;
    }

    public IReadOnlyList<T> Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        var checkpoint = context.Checkpoint(ref reader);
        try
        {
            if (_count > context.Options.MaxSequenceElements)
                throw new LimitExceededException(
                    CodecErrorCode.SequenceTooLarge,
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    "SequenceElements", _count, context.Options.MaxSequenceElements);

            var items = ArrayPool<T>.Shared.Rent(_count);
            var count = 0;
            try
            {
                using var depthGuard = context.PushDepth();
                for (int i = 0; i < _count; i++)
                {
                    using var pathGuard = context.PushPath($"[{i}]");
                    items[i] = _elementCodec.Decode(ref reader, context);
                }
                count = _count;
                return new PooledArray<T>(items, count);
            }
            catch
            {
                ArrayPool<T>.Shared.Return(items, clearArray: true);
                throw;
            }
        }
        catch
        {
            context.Rewind(ref reader, checkpoint);
            throw;
        }
    }

    public void Encode(IReadOnlyList<T> value, IBufferWriter<byte> writer, CodecContext context)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (value.Count != _count)
            throw new CodecValidationException(0, context.CurrentPath,
                $"Expected {_count} elements but got {value.Count}");

        for (int i = 0; i < value.Count; i++)
        {
            using var depthGuard = context.PushDepth();
            _elementCodec.Encode(value[i], writer, context);
        }
    }
}

/// <summary>
/// Decodes/encodes a count-prefixed list: [count][elements...].
/// </summary>
internal sealed class RepeatPrefixedCodec<T> : ICodec<IReadOnlyList<T>>
{
    private readonly ICodec<T> _elementCodec;
    private readonly ICodec<long> _countCodec;

    public RepeatPrefixedCodec(ICodec<T> elementCodec, ICodec<long> countCodec)
    {
        _elementCodec = elementCodec ?? throw new ArgumentNullException(nameof(elementCodec));
        _countCodec = countCodec ?? throw new ArgumentNullException(nameof(countCodec));
    }

    public IReadOnlyList<T> Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        var checkpoint = context.Checkpoint(ref reader);
        try
        {
            long count = _countCodec.Decode(ref reader, context);

            if (count < 0)
                throw new CodecValidationException(
                    CodecErrorCode.InvalidValue,
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    $"Negative element count: {count}");

            if (count > context.Options.MaxSequenceElements)
                throw new LimitExceededException(
                    CodecErrorCode.SequenceTooLarge,
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    "SequenceElements", count, context.Options.MaxSequenceElements);

            int n = (int)count;
            var items = ArrayPool<T>.Shared.Rent(n);
            try
            {
                for (int i = 0; i < n; i++)
                {
                    using var depthGuard = context.PushDepth();
                    items[i] = _elementCodec.Decode(ref reader, context);
                }
                return new PooledArray<T>(items, n);
            }
            catch
            {
                ArrayPool<T>.Shared.Return(items, clearArray: true);
                throw;
            }
        }
        catch
        {
            context.Rewind(ref reader, checkpoint);
            throw;
        }
    }

    public void Encode(IReadOnlyList<T> value, IBufferWriter<byte> writer, CodecContext context)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));

        _countCodec.Encode(value.Count, writer, context);

        for (int i = 0; i < value.Count; i++)
        {
            using var depthGuard = context.PushDepth();
            _elementCodec.Encode(value[i], writer, context);
        }
    }
}

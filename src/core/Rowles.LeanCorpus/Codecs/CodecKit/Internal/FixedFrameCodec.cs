using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// Fixed-frame framing: [body][padding] with exactly <c>size</c> total bytes.
/// </summary>
internal sealed class FixedFrameCodec<T> : ICodec<T>
{
    private readonly int _size;
    private readonly ICodec<T> _innerCodec;
    private readonly FramePadding _padding;
    private readonly TrailingDataPolicy _trailingDataPolicy;

    public FixedFrameCodec(int size, ICodec<T> innerCodec, FramePadding padding, TrailingDataPolicy trailingDataPolicy)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size), "Frame size must be positive.");
        _size = size;
        _innerCodec = innerCodec ?? throw new ArgumentNullException(nameof(innerCodec));
        _padding = padding ?? throw new ArgumentNullException(nameof(padding));
        _trailingDataPolicy = trailingDataPolicy;
    }

    public T Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        var checkpoint = context.Checkpoint(ref reader);

        try
        {
            using var pathGuard = context.PushPath($"{{frame:{_size}}}");

            if (reader.Remaining < _size)
                throw new InsufficientDataException(
                    context.GetByteOffset(ref reader), context.CurrentPath, _size, (int)reader.Remaining);

            // Slice the frame from the reader
            var frameSequence = reader.Sequence.Slice(reader.Position, _size);
            var frameReader = new SequenceReader<byte>(frameSequence);

            using var scope = context.EnterScope(_size);
            using var depthGuard = context.PushDepth();
            T value = _innerCodec.Decode(ref frameReader, context);

            long trailingBytes = frameReader.Remaining;

            if (trailingBytes > 0)
            {
                if (_padding.IsExact)
                {
                    throw new TrailingDataException(
                        context.GetByteOffset(ref reader) + frameReader.Consumed,
                        context.CurrentPath, (int)trailingBytes);
                }

                byte expectedByte = _padding.FillByte ?? 0x00;
                int paddingLen = (int)frameReader.Remaining;

                if (paddingLen <= 512)
                {
                    Span<byte> paddingBuf = stackalloc byte[paddingLen];
                    frameReader.TryCopyTo(paddingBuf);
                    int badIdx = IndexOfBadByte(paddingBuf, expectedByte);
                    if (badIdx >= 0)
                        throw new InvalidPaddingException(
                            context.GetByteOffset(ref reader) + frameReader.Consumed + badIdx,
                            context.CurrentPath, expectedByte, paddingBuf[badIdx]);
                }
                else
                {
                    byte[] rented = ArrayPool<byte>.Shared.Rent(paddingLen);
                    try
                    {
                        Span<byte> paddingBuf = rented.AsSpan(0, paddingLen);
                        frameReader.TryCopyTo(paddingBuf);
                        int badIdx = IndexOfBadByte(paddingBuf, expectedByte);
                        if (badIdx >= 0)
                            throw new InvalidPaddingException(
                                context.GetByteOffset(ref reader) + frameReader.Consumed + badIdx,
                                context.CurrentPath, expectedByte, paddingBuf[badIdx]);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                }
            }

            // Advance outer reader past the entire frame
            reader.Advance(_size);

            // Enforce trailing data policy beyond the fixed frame
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

        // Stage payload to scratch buffer
        var scratch = context.RentScratchBuffer(_size);
        try
        {

            using var depthGuard = context.PushDepth();
            _innerCodec.Encode(value, scratch, context);

            int payloadLength = scratch.Length;
            if (payloadLength > _size)
            {
                throw new FrameOverflowException(
                    0, context.CurrentPath, _size, payloadLength);
            }

            // Write the payload
            var written = scratch.Written;
            foreach (var segment in written)
            {
                var span = writer.GetSpan(segment.Length);
                segment.Span.CopyTo(span);
                writer.Advance(segment.Length);
            }

            // Write padding
            int paddingBytes = _size - payloadLength;
            if (paddingBytes > 0)
            {
                if (_padding.IsExact)
                {
                    throw new FrameOverflowException(
                        0, context.CurrentPath, _size, payloadLength);
                }

                byte fillByte = _padding.FillByte ?? 0x00;

                var padSpan = writer.GetSpan(paddingBytes);
                padSpan.Slice(0, paddingBytes).Fill(fillByte);
                writer.Advance(paddingBytes);
            }
        }
        finally
        {
            context.ReturnScratchBuffer(scratch);
        }
    }

    private static int IndexOfBadByte(ReadOnlySpan<byte> buf, byte expected)
    {
#if NET8_0_OR_GREATER
        return buf.IndexOfAnyExcept(expected);
#else
        for (int i = 0; i < buf.Length; i++)
            if (buf[i] != expected) return i;
        return -1;
#endif
    }
}

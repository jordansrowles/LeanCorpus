using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// Wraps an inner codec with checksum computation and verification.
/// Wire format depends on <see cref="ChecksumPlacement"/>:
///   Header:  [checksum][body]
///   Trailer: [body][checksum]
/// </summary>
internal sealed class WithChecksumCodec<T> : ICodec<T>
{
    private readonly ChecksumAlgorithmId _algorithmId;
    private readonly ChecksumPlacement _placement;
    private readonly ICodec<T> _innerCodec;

    public WithChecksumCodec(ChecksumAlgorithmId algorithmId, ChecksumPlacement placement, ICodec<T> innerCodec)
    {
        _algorithmId = algorithmId ?? throw new ArgumentNullException(nameof(algorithmId));
        _innerCodec = innerCodec ?? throw new ArgumentNullException(nameof(innerCodec));
        _placement = placement;
    }

    public T Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        var checkpoint = context.Checkpoint(ref reader);
        try
        {
            using var pathGuard = context.PushPath($"{{checksum:{_algorithmId.Name}}}");
            var provider = context.Registry.GetChecksumProvider(_algorithmId);
            int checksumLen = provider.ChecksumByteLength;

            if (_placement == ChecksumPlacement.Trailer)
            {
                // [body][checksum]
                var bodyStart = reader.Position;
                using var depthGuard = context.PushDepth();
                T value = _innerCodec.Decode(ref reader, context);
                var bodyEnd = reader.Position;
                var bodyBytes = reader.Sequence.Slice(bodyStart, bodyEnd);

                if (reader.Remaining < checksumLen)
                    throw new InsufficientDataException(
                        context.GetByteOffset(ref reader), context.CurrentPath,
                        checksumLen, (int)reader.Remaining);

                var checksumSlice = reader.Sequence.Slice(reader.Position, checksumLen);
                byte[] expectedChecksum = checksumSlice.ToArray();
                reader.Advance(checksumLen);

                if (!provider.Verify(bodyBytes, expectedChecksum))
                {
                    byte[] actual = provider.Compute(bodyBytes);
                    throw new ChecksumMismatchException(
                        context.GetByteOffset(ref reader) - checksumLen,
                        context.CurrentPath, _algorithmId.Name,
                        expectedChecksum, actual);
                }

                return value;
            }
            else
            {
                // Header: [checksum][body]
                if (reader.Remaining < checksumLen)
                    throw new InsufficientDataException(
                        context.GetByteOffset(ref reader), context.CurrentPath,
                        checksumLen, (int)reader.Remaining);

                var checksumSlice = reader.Sequence.Slice(reader.Position, checksumLen);
                byte[] expectedChecksum = checksumSlice.ToArray();
                reader.Advance(checksumLen);

                var bodyStart = reader.Position;
                using var depthGuard = context.PushDepth();
                T value = _innerCodec.Decode(ref reader, context);
                var bodyEnd = reader.Position;
                var bodyBytes = reader.Sequence.Slice(bodyStart, bodyEnd);

                if (!provider.Verify(bodyBytes, expectedChecksum))
                {
                    byte[] actual = provider.Compute(bodyBytes);
                    throw new ChecksumMismatchException(
                        context.GetByteOffset(ref reader),
                        context.CurrentPath, _algorithmId.Name,
                        expectedChecksum, actual);
                }

                return value;
            }
        }
        catch
        {
            context.Rewind(ref reader, checkpoint);
            throw;
        }
    }

    public void Encode(T value, IBufferWriter<byte> writer, CodecContext context)
    {
        var provider = context.Registry.GetChecksumProvider(_algorithmId);

        // Stage inner codec to scratch buffer
        var scratch = context.RentScratchBuffer();
        try
        {

            using var depthGuard = context.PushDepth();
            _innerCodec.Encode(value, scratch, context);

            var stagedBytes = scratch.Written;
            byte[] checksum = provider.Compute(stagedBytes);

            if (_placement == ChecksumPlacement.Header)
            {
                // Write checksum first, then body
                var checksumSpan = writer.GetSpan(checksum.Length);
                checksum.AsSpan().CopyTo(checksumSpan);
                writer.Advance(checksum.Length);

                foreach (var segment in stagedBytes)
                {
                    var span = writer.GetSpan(segment.Length);
                    segment.Span.CopyTo(span);
                    writer.Advance(segment.Length);
                }
            }
            else
            {
                // Write body first, then checksum
                foreach (var segment in stagedBytes)
                {
                    var span = writer.GetSpan(segment.Length);
                    segment.Span.CopyTo(span);
                    writer.Advance(segment.Length);
                }

                var checksumSpan = writer.GetSpan(checksum.Length);
                checksum.AsSpan().CopyTo(checksumSpan);
                writer.Advance(checksum.Length);
            }
        }
        finally
        {
            context.ReturnScratchBuffer(scratch);
        }
    }
}

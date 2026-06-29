using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Compression;
namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// Wraps an inner codec with Deflate compression/decompression.
/// Wire format: [compressed-length][compressed-payload]
/// </summary>
internal sealed class WithCompressionCodec<T> : ICodec<T>
{
    private readonly CodecCompressionLevel _level;
    private readonly ICodec<long> _compressedLengthCodec;
    private readonly ICodec<T> _innerCodec;

    public WithCompressionCodec(
        CodecCompressionLevel level,
        ICodec<long> compressedLengthCodec,
        ICodec<T> innerCodec)
    {
        _compressedLengthCodec = compressedLengthCodec ?? throw new ArgumentNullException(nameof(compressedLengthCodec));
        _innerCodec = innerCodec ?? throw new ArgumentNullException(nameof(innerCodec));
        _level = level;
    }

    public T Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        var checkpoint = context.Checkpoint(ref reader);
        try
        {
            using var pathGuard = context.PushPath("{compressed:deflate}");

            long compressedLength = _compressedLengthCodec.Decode(ref reader, context);

            if (compressedLength < 0)
                throw new CodecValidationException(
                    CodecErrorCode.NegativeLength,
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    $"Negative compressed length: {compressedLength}");

            if (compressedLength > context.Options.MaxFrameBytes)
                throw new LimitExceededException(
                    CodecErrorCode.FrameTooLarge,
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    "CompressedLength", compressedLength, context.Options.MaxFrameBytes);

            int compLen = (int)compressedLength;
            if (reader.Remaining < compLen)
                throw new InsufficientDataException(
                    context.GetByteOffset(ref reader),
                    context.CurrentPath, compLen, (int)reader.Remaining);

            var compressedSequence = reader.Sequence.Slice(reader.Position, compLen);
            reader.Advance(compLen);

            byte[] decompressed;
            try
            {
                if (compressedSequence.IsSingleSegment)
                    decompressed = Decompress(compressedSequence.FirstSpan);
                else
                    decompressed = Decompress(compressedSequence.ToArray());
            }
            catch (Exception ex) when (ex is not CodecException)
            {
                throw new CodecValidationException(
                    CodecErrorCode.DecompressionFailed,
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    $"Decompression failed: {ex.Message}", ex);
            }

            if (decompressed.Length > context.Options.MaxDecompressedBytes)
                throw new LimitExceededException(
                    CodecErrorCode.DecompressionLimitExceeded,
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    "DecompressedLength", decompressed.Length, context.Options.MaxDecompressedBytes);

            var decompressedSequence = new ReadOnlySequence<byte>(decompressed);
            var subReader = new SequenceReader<byte>(decompressedSequence);

            using var offsetGuard = context.SetByteOffsetBase(0);
            using var innerPathGuard = context.PushPath("{decompressed}");
            using var depthGuard = context.PushDepth();
            T value = _innerCodec.Decode(ref subReader, context);
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

        // Stage inner encode to scratch buffer
        var scratch = context.RentScratchBuffer();
        try
        {

            using var depthGuard = context.PushDepth();
            _innerCodec.Encode(value, scratch, context);

            var stagedBytes = scratch.Written;
            byte[] compressed = stagedBytes.IsSingleSegment
                ? Compress(stagedBytes.FirstSpan)
                : Compress(stagedBytes.ToArray());

            // Write compressed length
            _compressedLengthCodec.Encode(compressed.Length, writer, context);

            // Write compressed bytes
            var span = writer.GetSpan(compressed.Length);
            compressed.AsSpan().CopyTo(span);
            writer.Advance(compressed.Length);
        }
        finally
        {
            context.ReturnScratchBuffer(scratch);
        }
    }

    private byte[] Compress(ReadOnlySpan<byte> data)
    {
        var cl = _level switch
        {
            CodecCompressionLevel.Fastest => CompressionLevel.Fastest,
            CodecCompressionLevel.Optimal => CompressionLevel.Optimal,
            CodecCompressionLevel.SmallestSize => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal
        };
        using var output = new MemoryStream(Math.Max(data.Length / 2, 256));
        using (var deflate = new DeflateStream(output, cl, leaveOpen: true))
            deflate.Write(data);
        return output.ToArray();
    }

    private static byte[] Decompress(ReadOnlySpan<byte> compressedData)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(compressedData.Length);
        try
        {
            compressedData.CopyTo(rented);
            using var input = new MemoryStream(rented, 0, compressedData.Length, false);
            using var output = new MemoryStream(compressedData.Length * 4);
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                deflate.CopyTo(output);
            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}

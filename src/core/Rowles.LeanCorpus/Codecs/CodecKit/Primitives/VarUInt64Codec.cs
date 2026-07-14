using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class VarUInt64Codec : ICodec<ulong>
{
    private const int MaxBytes = 10;

    public ulong Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        var checkpoint = context.Checkpoint(ref reader);

        ulong result = 0;
        int shift = 0;
        int bytesRead = 0;

        while (true)
        {
            if (!reader.TryRead(out byte b))
            {
                context.Rewind(ref reader, checkpoint);
                throw new InsufficientDataException(offset, context.CurrentPath, bytesRead + 1, bytesRead);
            }

            bytesRead++;

            if (bytesRead == MaxBytes)
            {
                // 10th byte: only lowest bit is valid for uint64
                if (b > 0x01)
                {
                    context.Rewind(ref reader, checkpoint);
                    throw new CodecValidationException(
                        CodecErrorCode.Overflow, offset, context.CurrentPath,
                        $"VarUInt64 overflow at offset {offset}: 10th byte 0x{b:X2} exceeds 1-bit limit.");
                }
            }

            result |= (ulong)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
                break;

            shift += 7;

            if (bytesRead >= MaxBytes)
            {
                context.Rewind(ref reader, checkpoint);
                throw new CodecValidationException(
                    CodecErrorCode.Overflow, offset, context.CurrentPath,
                    $"VarUInt64 overflow at offset {offset}: exceeded {MaxBytes} bytes.");
            }
        }

        if (bytesRead > 1 && context.Options.RejectNonCanonicalVarInts)
        {
            int minBytesNeeded = result == 0 ? 1 : ((64 - LeadingZeroCount(result) + 6) / 7);
            if (bytesRead > minBytesNeeded)
            {
                context.Rewind(ref reader, checkpoint);
                throw new CodecValidationException(
                    CodecErrorCode.Overflow, offset, context.CurrentPath,
                    $"Non-canonical VarUInt64 at offset {offset}: used {bytesRead} bytes but {minBytesNeeded} would suffice.");
            }
        }

        return result;
    }

    public void Encode(ulong value, IBufferWriter<byte> writer, CodecContext context)
    {
        Span<byte> buf = stackalloc byte[MaxBytes];
        int count = 0;
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
                b |= 0x80;
            buf[count++] = b;
        } while (value != 0);

        var span = writer.GetSpan(count);
        buf.Slice(0, count).CopyTo(span);
        writer.Advance(count);
    }

    private static int LeadingZeroCount(ulong value)
    {
#if NET8_0_OR_GREATER
        return System.Numerics.BitOperations.LeadingZeroCount(value);
#else
        if (value == 0) return 64;
        int n = 0;
        if (value <= 0x00000000FFFFFFFF) { n += 32; value <<= 32; }
        if (value <= 0x0000FFFFFFFFFFFF) { n += 16; value <<= 16; }
        if (value <= 0x00FFFFFFFFFFFFFF) { n += 8; value <<= 8; }
        if (value <= 0x0FFFFFFFFFFFFFFF) { n += 4; value <<= 4; }
        if (value <= 0x3FFFFFFFFFFFFFFF) { n += 2; value <<= 2; }
        if (value <= 0x7FFFFFFFFFFFFFFF) { n += 1; }
        return n;
#endif
    }
}

using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class VarUInt32Codec : ICodec<uint>
{
    private const int MaxBytes = 5;

    public uint Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        var checkpoint = context.Checkpoint(ref reader);

        uint result = 0;
        int shift = 0;
        int bytesRead = 0;
        // Track the last byte for non-canonical detection
        int lastSignificantByteIndex = -1;

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
                // 5th byte: only lower 4 bits are valid for uint32
                if (b > 0x0F)
                {
                    context.Rewind(ref reader, checkpoint);
                    throw new CodecValidationException(
                        CodecErrorCode.Overflow, offset, context.CurrentPath,
                        $"VarUInt32 overflow at offset {offset}: 5th byte 0x{b:X2} exceeds 4-bit limit.");
                }
            }

            result |= (uint)(b & 0x7F) << shift;

            if ((b & 0x7F) != 0)
                lastSignificantByteIndex = bytesRead;

            if ((b & 0x80) == 0)
                break;

            shift += 7;

            if (bytesRead >= MaxBytes)
            {
                context.Rewind(ref reader, checkpoint);
                throw new CodecValidationException(
                    CodecErrorCode.Overflow, offset, context.CurrentPath,
                    $"VarUInt32 overflow at offset {offset}: exceeded {MaxBytes} bytes.");
            }
        }

        if (bytesRead > 1 && context.Options.RejectNonCanonicalVarInts)
        {
            // Non-canonical if there are trailing zero continuation bytes beyond the last significant byte.
            // Canonical = shortest possible encoding. If the last byte is 0x00 and we had a continuation before,
            // the value could have been encoded with fewer bytes.
            int minBytesNeeded = result == 0 ? 1 : ((32 - LeadingZeroCount(result) + 6) / 7);
            if (bytesRead > minBytesNeeded)
            {
                context.Rewind(ref reader, checkpoint);
                throw new CodecValidationException(
                    CodecErrorCode.Overflow, offset, context.CurrentPath,
                    $"Non-canonical VarUInt32 at offset {offset}: used {bytesRead} bytes but {minBytesNeeded} would suffice.");
            }
        }

        return result;
    }

    public void Encode(uint value, IBufferWriter<byte> writer, CodecContext context)
    {
        // LEB128: at most 5 bytes for uint32
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

    private static int LeadingZeroCount(uint value)
    {
#if NET8_0_OR_GREATER
        return System.Numerics.BitOperations.LeadingZeroCount(value);
#else
        if (value == 0) return 32;
        int n = 0;
        if (value <= 0x0000FFFF) { n += 16; value <<= 16; }
        if (value <= 0x00FFFFFF) { n += 8; value <<= 8; }
        if (value <= 0x0FFFFFFF) { n += 4; value <<= 4; }
        if (value <= 0x3FFFFFFF) { n += 2; value <<= 2; }
        if (value <= 0x7FFFFFFF) { n += 1; }
        return n;
#endif
    }
}

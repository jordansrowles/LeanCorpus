using System;
using System.Buffers;
using System.Buffers.Binary;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class Float32LECodec : ICodec<float>
{
    private const int Size = 4;

    public float Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        int available = (int)reader.Remaining;
        if (!reader.TryReadLittleEndian(out int bits))
            throw new InsufficientDataException(offset, context.CurrentPath, Size, available);
        return BitConverter.Int32BitsToSingle(bits);
    }

    public void Encode(float value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(Size);
        int bits = BitConverter.SingleToInt32Bits(value);
        BinaryPrimitives.WriteInt32LittleEndian(span, bits);
        writer.Advance(Size);
    }
}

internal sealed class Float32BECodec : ICodec<float>
{
    private const int Size = 4;

    public float Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        int available = (int)reader.Remaining;
        if (!reader.TryReadBigEndian(out int bits))
            throw new InsufficientDataException(offset, context.CurrentPath, Size, available);
        return BitConverter.Int32BitsToSingle(bits);
    }

    public void Encode(float value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(Size);
        int bits = BitConverter.SingleToInt32Bits(value);
        BinaryPrimitives.WriteInt32BigEndian(span, bits);
        writer.Advance(Size);
    }
}

using System;
using System.Buffers;
using System.Buffers.Binary;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class Float64LECodec : ICodec<double>
{
    private const int Size = 8;

    public double Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        int available = (int)reader.Remaining;
        if (!reader.TryReadLittleEndian(out long bits))
            throw new InsufficientDataException(offset, context.CurrentPath, Size, available);
        return BitConverter.Int64BitsToDouble(bits);
    }

    public void Encode(double value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(Size);
        long bits = BitConverter.DoubleToInt64Bits(value);
        BinaryPrimitives.WriteInt64LittleEndian(span, bits);
        writer.Advance(Size);
    }
}

internal sealed class Float64BECodec : ICodec<double>
{
    private const int Size = 8;

    public double Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        int available = (int)reader.Remaining;
        if (!reader.TryReadBigEndian(out long bits))
            throw new InsufficientDataException(offset, context.CurrentPath, Size, available);
        return BitConverter.Int64BitsToDouble(bits);
    }

    public void Encode(double value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(Size);
        long bits = BitConverter.DoubleToInt64Bits(value);
        BinaryPrimitives.WriteInt64BigEndian(span, bits);
        writer.Advance(Size);
    }
}

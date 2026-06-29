using System;
using System.Buffers;
using System.Buffers.Binary;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class UInt16LECodec : ICodec<ushort>
{
    private const int Size = 2;

    public ushort Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        int available = (int)reader.Remaining;
        if (!reader.TryReadLittleEndian(out short raw))
            throw new InsufficientDataException(offset, context.CurrentPath, Size, available);
        return (ushort)raw;
    }

    public void Encode(ushort value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(Size);
        BinaryPrimitives.WriteUInt16LittleEndian(span, value);
        writer.Advance(Size);
    }
}

internal sealed class UInt16BECodec : ICodec<ushort>
{
    private const int Size = 2;

    public ushort Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        int available = (int)reader.Remaining;
        if (!reader.TryReadBigEndian(out short raw))
            throw new InsufficientDataException(offset, context.CurrentPath, Size, available);
        return (ushort)raw;
    }

    public void Encode(ushort value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(Size);
        BinaryPrimitives.WriteUInt16BigEndian(span, value);
        writer.Advance(Size);
    }
}

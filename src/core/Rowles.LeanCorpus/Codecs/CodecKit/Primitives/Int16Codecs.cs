using System;
using System.Buffers;
using System.Buffers.Binary;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class Int16LECodec : ICodec<short>
{
    private const int Size = 2;

    public short Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        int available = (int)reader.Remaining;
        if (!reader.TryReadLittleEndian(out short raw))
            throw new InsufficientDataException(offset, context.CurrentPath, Size, available);
        return raw;
    }

    public void Encode(short value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(Size);
        BinaryPrimitives.WriteInt16LittleEndian(span, value);
        writer.Advance(Size);
    }
}

internal sealed class Int16BECodec : ICodec<short>
{
    private const int Size = 2;

    public short Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        int available = (int)reader.Remaining;
        if (!reader.TryReadBigEndian(out short raw))
            throw new InsufficientDataException(offset, context.CurrentPath, Size, available);
        return raw;
    }

    public void Encode(short value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(Size);
        BinaryPrimitives.WriteInt16BigEndian(span, value);
        writer.Advance(Size);
    }
}

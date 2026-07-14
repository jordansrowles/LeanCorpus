using System;
using System.Buffers;
using System.Buffers.Binary;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class UInt64LECodec : ICodec<ulong>
{
    private const int Size = 8;

    public ulong Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        int available = (int)reader.Remaining;
        if (!reader.TryReadLittleEndian(out long raw))
            throw new InsufficientDataException(offset, context.CurrentPath, Size, available);
        return (ulong)raw;
    }

    public void Encode(ulong value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(Size);
        BinaryPrimitives.WriteUInt64LittleEndian(span, value);
        writer.Advance(Size);
    }
}

internal sealed class UInt64BECodec : ICodec<ulong>
{
    private const int Size = 8;

    public ulong Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        int available = (int)reader.Remaining;
        if (!reader.TryReadBigEndian(out long raw))
            throw new InsufficientDataException(offset, context.CurrentPath, Size, available);
        return (ulong)raw;
    }

    public void Encode(ulong value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(Size);
        BinaryPrimitives.WriteUInt64BigEndian(span, value);
        writer.Advance(Size);
    }
}

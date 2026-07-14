using System;
using System.Buffers;
using System.Buffers.Binary;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class UInt32LECodec : ICodec<uint>
{
    private const int Size = 4;

    public uint Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        int available = (int)reader.Remaining;
        if (!reader.TryReadLittleEndian(out int raw))
            throw new InsufficientDataException(offset, context.CurrentPath, Size, available);
        return (uint)raw;
    }

    public void Encode(uint value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(Size);
        BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        writer.Advance(Size);
    }
}

internal sealed class UInt32BECodec : ICodec<uint>
{
    private const int Size = 4;

    public uint Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        int available = (int)reader.Remaining;
        if (!reader.TryReadBigEndian(out int raw))
            throw new InsufficientDataException(offset, context.CurrentPath, Size, available);
        return (uint)raw;
    }

    public void Encode(uint value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(Size);
        BinaryPrimitives.WriteUInt32BigEndian(span, value);
        writer.Advance(Size);
    }
}

using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class UInt8Codec : ICodec<byte>
{
    public byte Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        if (!reader.TryRead(out byte b))
            throw new InsufficientDataException(offset, context.CurrentPath, 1, 0);
        return b;
    }

    public void Encode(byte value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(1);
        span[0] = value;
        writer.Advance(1);
    }
}

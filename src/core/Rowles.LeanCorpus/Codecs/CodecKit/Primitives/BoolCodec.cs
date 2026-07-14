using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class BoolCodec : ICodec<bool>
{
    public bool Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);

        if (!reader.TryRead(out byte b))
            throw new InsufficientDataException(offset, context.CurrentPath, 1, 0);

        return b switch
        {
            0x00 => false,
            0x01 => true,
            _ => throw new InvalidBooleanException(offset, context.CurrentPath, b),
        };
    }

    public void Encode(bool value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(1);
        span[0] = value ? (byte)0x01 : (byte)0x00;
        writer.Advance(1);
    }
}

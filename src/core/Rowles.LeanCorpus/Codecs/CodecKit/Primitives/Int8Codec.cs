using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class Int8Codec : ICodec<sbyte>
{
    public sbyte Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        if (!reader.TryRead(out byte b))
            throw new InsufficientDataException(offset, context.CurrentPath, 1, 0);
        return unchecked((sbyte)b);
    }

    public void Encode(sbyte value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(1);
        span[0] = unchecked((byte)value);
        writer.Advance(1);
    }
}

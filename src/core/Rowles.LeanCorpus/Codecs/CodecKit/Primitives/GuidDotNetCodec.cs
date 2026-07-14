using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class GuidDotNetCodec : ICodec<Guid>
{
    private const int GuidSize = 16;

    public Guid Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);

        Span<byte> buf = stackalloc byte[GuidSize];
        if (!reader.TryCopyTo(buf))
            throw new InsufficientDataException(offset, context.CurrentPath, GuidSize, (int)reader.Remaining);
        reader.Advance(GuidSize);

#if NETSTANDARD2_1
        return new Guid(buf.ToArray());
#else
        return new Guid(buf);
#endif
    }

    public void Encode(Guid value, IBufferWriter<byte> writer, CodecContext context)
    {
        var span = writer.GetSpan(GuidSize);

#if NETSTANDARD2_1
        byte[] bytes = value.ToByteArray();
        bytes.AsSpan().CopyTo(span);
#else
        value.TryWriteBytes(span);
#endif

        writer.Advance(GuidSize);
    }
}

using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class GuidRfc4122Codec : ICodec<Guid>
{
    private const int GuidSize = 16;

    public Guid Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);

        Span<byte> rfc = stackalloc byte[GuidSize];
        if (!reader.TryCopyTo(rfc))
            throw new InsufficientDataException(offset, context.CurrentPath, GuidSize, (int)reader.Remaining);
        reader.Advance(GuidSize);

        // RFC 4122 -> .NET: reverse first 3 fields
        Span<byte> dotnet = stackalloc byte[GuidSize];
        // a (4 bytes) — reversed
        dotnet[0] = rfc[3]; dotnet[1] = rfc[2]; dotnet[2] = rfc[1]; dotnet[3] = rfc[0];
        // b (2 bytes) — reversed
        dotnet[4] = rfc[5]; dotnet[5] = rfc[4];
        // c (2 bytes) — reversed
        dotnet[6] = rfc[7]; dotnet[7] = rfc[6];
        // d-k (8 bytes) — as-is
        rfc.Slice(8, 8).CopyTo(dotnet.Slice(8, 8));

#if NETSTANDARD2_1
        return new Guid(dotnet.ToArray());
#else
        return new Guid(dotnet);
#endif
    }

    public void Encode(Guid value, IBufferWriter<byte> writer, CodecContext context)
    {
#if NETSTANDARD2_1
        byte[] dotnet = value.ToByteArray();
#else
        Span<byte> dotnet = stackalloc byte[GuidSize];
        value.TryWriteBytes(dotnet);
#endif

        var span = writer.GetSpan(GuidSize);

        // .NET -> RFC 4122: reverse first 3 fields
        span[0] = dotnet[3]; span[1] = dotnet[2]; span[2] = dotnet[1]; span[3] = dotnet[0];
        span[4] = dotnet[5]; span[5] = dotnet[4];
        span[6] = dotnet[7]; span[7] = dotnet[6];

        for (int i = 8; i < GuidSize; i++)
            span[i] = dotnet[i];

        writer.Advance(GuidSize);
    }
}

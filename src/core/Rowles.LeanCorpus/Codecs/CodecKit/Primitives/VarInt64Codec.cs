using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class VarInt64Codec : ICodec<long>
{
    private static readonly VarUInt64Codec UnsignedCodec = new();

    public long Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        ulong raw = UnsignedCodec.Decode(ref reader, context);
        return ZigZagDecode(raw);
    }

    public void Encode(long value, IBufferWriter<byte> writer, CodecContext context)
    {
        ulong encoded = ZigZagEncode(value);
        UnsignedCodec.Encode(encoded, writer, context);
    }

    private static ulong ZigZagEncode(long value) => (ulong)((value << 1) ^ (value >> 63));

    private static long ZigZagDecode(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);
}

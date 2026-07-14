using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class VarInt32Codec : ICodec<int>
{
    private static readonly VarUInt32Codec UnsignedCodec = new();

    public int Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        uint raw = UnsignedCodec.Decode(ref reader, context);
        return ZigZagDecode(raw);
    }

    public void Encode(int value, IBufferWriter<byte> writer, CodecContext context)
    {
        uint encoded = ZigZagEncode(value);
        UnsignedCodec.Encode(encoded, writer, context);
    }

    private static uint ZigZagEncode(int value) => (uint)((value << 1) ^ (value >> 31));

    private static int ZigZagDecode(uint value) => (int)(value >> 1) ^ -(int)(value & 1);
}

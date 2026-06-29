using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class BytesOwnedCodec : ICodec<byte[]>
{
    private readonly int _length;

    public BytesOwnedCodec(int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), length, "Must be non-negative.");
        _length = length;
    }

    public byte[] Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);

        if (reader.Remaining < _length)
            throw new InsufficientDataException(offset, context.CurrentPath, _length, (int)reader.Remaining);

        byte[] result = new byte[_length];
        reader.TryCopyTo(result.AsSpan());
        reader.Advance(_length);
        return result;
    }

    public void Encode(byte[] value, IBufferWriter<byte> writer, CodecContext context)
    {
        if (value.Length != _length)
            throw new ArgumentException($"Expected {_length} bytes but got {value.Length}.", nameof(value));

        if (_length == 0) return;
        var span = writer.GetSpan(_length);
        value.AsSpan().CopyTo(span);
        writer.Advance(_length);
    }
}

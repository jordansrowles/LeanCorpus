using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class PaddingCodec : ICodec<Unit>
{
    private readonly int _length;
    private readonly byte _value;

    public PaddingCodec(int length, byte value)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), length, "Must be non-negative.");
        _length = length;
        _value = value;
    }

    public Unit Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);

        if (reader.Remaining < _length)
            throw new InsufficientDataException(offset, context.CurrentPath, _length, (int)reader.Remaining);

        for (int i = 0; i < _length; i++)
        {
            reader.TryRead(out byte b);
            if (b != _value)
                throw new InvalidPaddingException(offset + i, context.CurrentPath, _value);
        }

        return Unit.Value;
    }

    public void Encode(Unit value, IBufferWriter<byte> writer, CodecContext context)
    {
        if (_length == 0) return;
        var span = writer.GetSpan(_length);
        span.Slice(0, _length).Fill(_value);
        writer.Advance(_length);
    }
}

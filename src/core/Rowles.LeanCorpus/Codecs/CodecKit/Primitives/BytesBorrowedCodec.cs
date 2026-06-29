using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class BytesBorrowedCodec : ICodec<ReadOnlySequence<byte>>
{
    private readonly int _length;

    public BytesBorrowedCodec(int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), length, "Must be non-negative.");
        _length = length;
    }

    public ReadOnlySequence<byte> Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);

        if (reader.Remaining < _length)
            throw new InsufficientDataException(offset, context.CurrentPath, _length, (int)reader.Remaining);

        var slice = reader.Sequence.Slice(reader.Position, _length);
        reader.Advance(_length);
        return slice;
    }

    public void Encode(ReadOnlySequence<byte> value, IBufferWriter<byte> writer, CodecContext context)
    {
        if (value.Length != _length)
            throw new ArgumentException($"Expected {_length} bytes but got {value.Length}.", nameof(value));

        foreach (var segment in value)
        {
            var span = writer.GetSpan(segment.Length);
            segment.Span.CopyTo(span);
            writer.Advance(segment.Length);
        }
    }
}

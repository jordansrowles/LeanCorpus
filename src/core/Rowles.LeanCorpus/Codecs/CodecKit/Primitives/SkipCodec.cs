using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class SkipCodec : ICodec<Unit>
{
    private readonly int _n;

    public SkipCodec(int n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n), n, "Must be non-negative.");
        _n = n;
    }

    public Unit Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);

        if (reader.Remaining < _n)
            throw new InsufficientDataException(offset, context.CurrentPath, _n, (int)reader.Remaining);
        reader.Advance(_n);

        return Unit.Value;
    }

    public void Encode(Unit value, IBufferWriter<byte> writer, CodecContext context)
    {
        if (_n == 0) return;
        var span = writer.GetSpan(_n);
        span.Slice(0, _n).Clear();
        writer.Advance(_n);
    }
}

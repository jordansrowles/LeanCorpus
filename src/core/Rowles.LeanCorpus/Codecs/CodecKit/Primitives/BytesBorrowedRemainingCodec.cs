using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class BytesBorrowedRemainingCodec : ICodec<ReadOnlySequence<byte>>
{
    public ReadOnlySequence<byte> Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);

        if (!context.InScope)
            throw new InvalidScopeException(offset, context.CurrentPath,
                "BytesBorrowedRemaining requires a delimited scope.");

        long remaining = context.RemainingInScope;
        if (reader.Remaining < remaining)
            throw new InsufficientDataException(offset, context.CurrentPath, (int)remaining, (int)reader.Remaining);

        var slice = reader.Sequence.Slice(reader.Position, remaining);
        reader.Advance(remaining);
        context.ConsumeScope(remaining);
        return slice;
    }

    public void Encode(ReadOnlySequence<byte> value, IBufferWriter<byte> writer, CodecContext context)
    {
        foreach (var segment in value)
        {
            var span = writer.GetSpan(segment.Length);
            segment.Span.CopyTo(span);
            writer.Advance(segment.Length);
        }
    }
}

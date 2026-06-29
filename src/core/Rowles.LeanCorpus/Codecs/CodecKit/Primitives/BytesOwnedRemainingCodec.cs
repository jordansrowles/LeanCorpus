using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class BytesOwnedRemainingCodec : ICodec<byte[]>
{
    public byte[] Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);

        if (!context.InScope)
            throw new InvalidScopeException(offset, context.CurrentPath,
                "BytesOwnedRemaining requires a delimited scope.");

        long remaining = context.RemainingInScope;
        if (reader.Remaining < remaining)
            throw new InsufficientDataException(offset, context.CurrentPath, (int)remaining, (int)reader.Remaining);

        byte[] result = new byte[remaining];
        reader.TryCopyTo(result.AsSpan());
        reader.Advance(remaining);
        context.ConsumeScope(remaining);
        return result;
    }

    public void Encode(byte[] value, IBufferWriter<byte> writer, CodecContext context)
    {
        if (value.Length == 0) return;
        var span = writer.GetSpan(value.Length);
        value.AsSpan().CopyTo(span);
        writer.Advance(value.Length);
    }
}

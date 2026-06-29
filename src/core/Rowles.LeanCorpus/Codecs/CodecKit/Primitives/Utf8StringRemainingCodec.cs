using System;
using System.Buffers;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class Utf8StringRemainingCodec : ICodec<string>
{
    public string Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);

        if (!context.InScope)
            throw new InvalidScopeException(offset, context.CurrentPath,
                "Utf8StringRemaining requires a delimited scope.");

        long remaining = context.RemainingInScope;

        if (remaining > context.Options.MaxStringBytes)
            throw new LimitExceededException(
                CodecErrorCode.Overflow, offset, context.CurrentPath,
                "MaxStringBytes", remaining, context.Options.MaxStringBytes);

        if (reader.Remaining < remaining)
            throw new InsufficientDataException(offset, context.CurrentPath, (int)remaining, (int)reader.Remaining);

        int length = (int)remaining;
        byte[] bytes = new byte[length];
        reader.TryCopyTo(bytes.AsSpan());
        reader.Advance(length);
        context.ConsumeScope(length);

        if (context.Options.Utf8Validation == Utf8ValidationMode.Strict)
        {
            if (!Utf8Helpers.IsValidUtf8(bytes))
                throw new InvalidUtf8Exception(offset, context.CurrentPath);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    public void Encode(string value, IBufferWriter<byte> writer, CodecContext context)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount == 0) return;

        var span = writer.GetSpan(byteCount);
#if NETSTANDARD2_1
        Encoding.UTF8.GetBytes(value.AsSpan(), span);
#else
        Encoding.UTF8.GetBytes(value, span);
#endif
        writer.Advance(byteCount);
    }
}

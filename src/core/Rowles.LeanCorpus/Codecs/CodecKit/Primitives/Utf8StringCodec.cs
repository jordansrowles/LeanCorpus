using System;
using System.Buffers;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class Utf8StringCodec : ICodec<string>
{
    private readonly int _byteLength;

    public Utf8StringCodec(int byteLength)
    {
        if (byteLength < 0) throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "Must be non-negative.");
        _byteLength = byteLength;
    }

    public string Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);

        if (_byteLength > context.Options.MaxStringBytes)
            throw new LimitExceededException(
                CodecErrorCode.Overflow, offset, context.CurrentPath,
                "MaxStringBytes", _byteLength, context.Options.MaxStringBytes);

        if (reader.Remaining < _byteLength)
            throw new InsufficientDataException(offset, context.CurrentPath, _byteLength, (int)reader.Remaining);

        byte[] bytes = new byte[_byteLength];
        reader.TryCopyTo(bytes.AsSpan());
        reader.Advance(_byteLength);

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
        if (byteCount != _byteLength)
            throw new ArgumentException(
                $"String encodes to {byteCount} UTF-8 bytes but codec expects {_byteLength}.", nameof(value));

        var span = writer.GetSpan(_byteLength);
#if NETSTANDARD2_1
        Encoding.UTF8.GetBytes(value.AsSpan(), span);
#else
        Encoding.UTF8.GetBytes(value, span);
#endif
        writer.Advance(_byteLength);
    }
}

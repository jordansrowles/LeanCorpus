using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

/// <summary>
/// Reads/writes a fixed-length byte sequence, validating that the bytes are well-formed UTF-8.
/// Returns an owned <c>byte[]</c> copy of the data.
/// </summary>
internal sealed class Utf8BytesOwnedCodec : ICodec<byte[]>
{
    private readonly int _byteLength;

    public Utf8BytesOwnedCodec(int byteLength)
    {
        if (byteLength < 0) throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "Must be non-negative.");
        _byteLength = byteLength;
    }

    public byte[] Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);

        if (_byteLength > context.Options.MaxStringBytes)
            throw new LimitExceededException(
                CodecErrorCode.Overflow, offset, context.CurrentPath,
                "MaxStringBytes", _byteLength, context.Options.MaxStringBytes);

        if (reader.Remaining < _byteLength)
            throw new InsufficientDataException(offset, context.CurrentPath, _byteLength, (int)reader.Remaining);

        byte[] result = new byte[_byteLength];
        reader.TryCopyTo(result.AsSpan());
        reader.Advance(_byteLength);

        if (context.Options.Utf8Validation == Utf8ValidationMode.Strict)
        {
            if (!Utf8Helpers.IsValidUtf8(result))
                throw new InvalidUtf8Exception(offset, context.CurrentPath);
        }

        return result;
    }

    public void Encode(byte[] value, IBufferWriter<byte> writer, CodecContext context)
    {
        if (value.Length != _byteLength)
            throw new ArgumentException(
                $"Expected {_byteLength} bytes but got {value.Length}.", nameof(value));

        if (context.Options.Utf8Validation == Utf8ValidationMode.Strict)
        {
            if (!Utf8Helpers.IsValidUtf8(value))
                throw new InvalidUtf8Exception(0, context.CurrentPath);
        }

        if (_byteLength == 0) return;
        var span = writer.GetSpan(_byteLength);
        value.AsSpan().CopyTo(span);
        writer.Advance(_byteLength);
    }
}

using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

/// <summary>
/// Reads/writes a fixed-length byte sequence, validating that the bytes are well-formed UTF-8.
/// Returns a borrowed <see cref="ReadOnlySequence{T}"/> slice of the input (zero-copy on decode).
/// </summary>
internal sealed class Utf8BytesBorrowedCodec : ICodec<ReadOnlySequence<byte>>
{
    private readonly int _byteLength;

    public Utf8BytesBorrowedCodec(int byteLength)
    {
        if (byteLength < 0) throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "Must be non-negative.");
        _byteLength = byteLength;
    }

    public ReadOnlySequence<byte> Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);

        if (_byteLength > context.Options.MaxStringBytes)
            throw new LimitExceededException(
                CodecErrorCode.Overflow, offset, context.CurrentPath,
                "MaxStringBytes", _byteLength, context.Options.MaxStringBytes);

        if (reader.Remaining < _byteLength)
            throw new InsufficientDataException(offset, context.CurrentPath, _byteLength, (int)reader.Remaining);

        var slice = reader.Sequence.Slice(reader.Position, _byteLength);

        if (context.Options.Utf8Validation == Utf8ValidationMode.Strict)
        {
            // Validate UTF-8 before returning the borrowed slice
            Span<byte> temp = _byteLength <= 256 ? stackalloc byte[_byteLength] : new byte[_byteLength];
            slice.CopyTo(temp);
            if (!Utf8Helpers.IsValidUtf8(temp.Slice(0, _byteLength)))
                throw new InvalidUtf8Exception(offset, context.CurrentPath);
        }

        reader.Advance(_byteLength);
        return slice;
    }

    public void Encode(ReadOnlySequence<byte> value, IBufferWriter<byte> writer, CodecContext context)
    {
        if (value.Length != _byteLength)
            throw new ArgumentException(
                $"Expected {_byteLength} bytes but got {value.Length}.", nameof(value));

        if (context.Options.Utf8Validation == Utf8ValidationMode.Strict)
        {
            Span<byte> temp = _byteLength <= 256 ? stackalloc byte[_byteLength] : new byte[_byteLength];
            value.CopyTo(temp);
            if (!Utf8Helpers.IsValidUtf8(temp.Slice(0, _byteLength)))
                throw new InvalidUtf8Exception(0, context.CurrentPath);
        }

        foreach (var segment in value)
        {
            var span = writer.GetSpan(segment.Length);
            segment.Span.CopyTo(span);
            writer.Advance(segment.Length);
        }
    }
}

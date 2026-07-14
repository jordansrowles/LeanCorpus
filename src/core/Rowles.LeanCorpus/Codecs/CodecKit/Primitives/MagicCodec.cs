using System;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

internal sealed class MagicCodec : ICodec<Unit>
{
    private readonly byte[] _pattern;

    public MagicCodec(byte[] pattern)
    {
        _pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
    }

    public MagicCodec(uint value)
    {
        _pattern = new byte[4];
        _pattern[0] = (byte)(value >> 24);
        _pattern[1] = (byte)(value >> 16);
        _pattern[2] = (byte)(value >> 8);
        _pattern[3] = (byte)value;
    }

    public Unit Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        long offset = context.GetByteOffset(ref reader);
        int len = _pattern.Length;

        if (reader.Remaining < len)
            throw new InsufficientDataException(offset, context.CurrentPath, len, (int)reader.Remaining);

        Span<byte> actual = len <= 256 ? stackalloc byte[len] : new byte[len];
        for (int i = 0; i < len; i++)
            reader.TryRead(out actual[i]);

        for (int i = 0; i < len; i++)
        {
            if (actual[i] != _pattern[i])
                throw new MagicMismatchException(offset, context.CurrentPath, _pattern, actual.ToArray());
        }

        return Unit.Value;
    }

    public void Encode(Unit value, IBufferWriter<byte> writer, CodecContext context)
    {
        int len = _pattern.Length;
        if (len == 0) return;
        var span = writer.GetSpan(len);
        _pattern.AsSpan().CopyTo(span);
        writer.Advance(len);
    }
}

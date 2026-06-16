using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>
/// CodecKit file header read/write for codec files.
/// CodecKit format: [byte version][VarInt64 bodyLen][body]
/// </summary>
internal static class CodecFileHeader
{
    /// <summary>
    /// The result of reading a codec file body, including the format version.
    /// </summary>
    internal readonly struct ReadResult
    {
        public byte[] Body { get; }
        public byte Version { get; }
        public ReadResult(byte[] body, byte version) { Body = body; Version = version; }
    }

    // ═══════════════════════════════════════════════════
    //  IndexOutput / IndexInput
    // ═══════════════════════════════════════════════════

    internal static ReadResult Read(IndexInput input, ICodec<byte[]> format)
    {
        long remaining = input.Length - input.Position;
        if (remaining < 1)
            throw new InvalidDataException("CodecKit file is truncated: no payload bytes found.");
        byte[] raw = new byte[remaining];
        for (long i = 0; i < remaining; i++)
            raw[i] = input.ReadByte();

        var seq = new ReadOnlySequence<byte>(raw);
        var reader = new SequenceReader<byte>(seq);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);
        byte[] body;
        try
        {
            body = format.Decode(ref reader, ctx);
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException("CodecKit file is corrupt or truncated.", ex);
        }

        byte version = raw[0];
        return new ReadResult(body, version);
    }

    internal static byte ReadVersion(IndexInput input, ICodec<byte[]> format)
    {
        byte version = input.ReadByte();
        SkipVarInt64(input);
        return version;
    }

    internal static void Write(IndexOutput output, ICodec<byte[]> format, byte[] body)
    {
        var writer = new Adapters.IndexOutputBuffer(output);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);
        format.Encode(body, writer, ctx);
    }

    /// <summary>
    /// Writes a codec file envelope with a <see cref="ReadOnlySpan{Byte}"/> body.
    /// Uses the <see cref="VersionEnvelopeCodec{TBase,TVersion}"/> fast path when
    /// available, avoiding the intermediate <c>byte[]</c> allocation and scratch staging.
    /// </summary>
    internal static void Write(IndexOutput output, ICodec<byte[]> format, ReadOnlySpan<byte> body)
    {
        var writer = new Adapters.IndexOutputBuffer(output);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);

        if (format is VersionEnvelopeCodec<byte[], byte> envelope)
        {
            envelope.EncodeSpan(body, writer, ctx);
        }
        else
        {
            // Fallback: allocate byte[] for non-envelope codecs (e.g. WithCompressionCodec).
            format.Encode(body.ToArray(), writer, ctx);
        }
    }

    internal static (T Value, byte Version) Read<T>(IndexInput input, ICodec<byte[]> format, ICodec<T> bodyCodec)
    {
        var result = Read(input, format);
        var seq = new ReadOnlySequence<byte>(result.Body);
        var reader = new SequenceReader<byte>(seq);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);
        T value = bodyCodec.Decode(ref reader, ctx);
        return (value, result.Version);
    }

    // ═══════════════════════════════════════════════════
    //  BinaryWriter / BinaryReader
    // ═══════════════════════════════════════════════════

    internal static void Write(BinaryWriter writer, ICodec<byte[]> format, byte[] body)
    {
        var buf = new ArrayBufferWriter<byte>(body.Length + 16);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);
        format.Encode(body, buf, ctx);
        writer.Write(buf.WrittenSpan);
    }

    /// <summary>
    /// <see cref="ReadOnlySpan{Byte}"/> overload for the <see cref="BinaryWriter"/> path.
    /// Uses the <see cref="VersionEnvelopeCodec{TBase,TVersion}"/> fast path when available.
    /// </summary>
    internal static void Write(BinaryWriter writer, ICodec<byte[]> format, ReadOnlySpan<byte> body)
    {
        var buf = new ArrayBufferWriter<byte>(body.Length + 16);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);

        if (format is VersionEnvelopeCodec<byte[], byte> envelope)
        {
            envelope.EncodeSpan(body, buf, ctx);
        }
        else
        {
            format.Encode(body.ToArray(), buf, ctx);
        }

        writer.Write(buf.WrittenSpan);
    }

    internal static ReadResult Read(BinaryReader reader, ICodec<byte[]> format)
    {
        long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
        if (remaining < 1)
            throw new InvalidDataException("CodecKit file is truncated: no payload bytes found.");
        byte[] raw = reader.ReadBytes((int)remaining);

        var seq = new ReadOnlySequence<byte>(raw);
        var seqReader = new SequenceReader<byte>(seq);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);
        byte[] body;
        try
        {
            body = format.Decode(ref seqReader, ctx);
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException("CodecKit file is corrupt or truncated.", ex);
        }

        byte version = raw[0];
        return new ReadResult(body, version);
    }

    internal static byte ReadVersion(BinaryReader reader, ICodec<byte[]> format)
    {
        byte version = reader.ReadByte();
        SkipVarInt64(reader);
        return version;
    }

    // ═══════════════════════════════════════════════════
    //  LEB128 VarInt64 skipping
    // ═══════════════════════════════════════════════════

    private static void SkipVarInt64(IndexInput input)
    {
        for (int i = 0; i < 10; i++)
        {
            byte b = input.ReadByte();
            if ((b & 0x80) == 0) return;
        }
        throw new InvalidDataException("VarInt64 body length is malformed (exceeds 10 bytes).");
    }

    private static void SkipVarInt64(BinaryReader reader)
    {
        for (int i = 0; i < 10; i++)
        {
            byte b = reader.ReadByte();
            if ((b & 0x80) == 0) return;
        }
        throw new InvalidDataException("VarInt64 body length is malformed (exceeds 10 bytes).");
    }
}

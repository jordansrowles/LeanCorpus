using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>
/// CodecKit file header read/write for codec files.
/// Supports two formats:
///   Envelope: [version:byte][VarInt64 bodyLen][body]
///   Trailer:  [version:byte][body][bodyLen:int64]  (8-byte fixed trailer)
/// </summary>
public static class CodecFileHeader
{
    public readonly struct ReadResult
    {
        public byte[] Body { get; }
        public byte Version { get; }
        public ReadResult(byte[] body, byte version) { Body = body; Version = version; }
    }

    // -- Streaming write (trailer format) -----------------------------------

    public sealed class StreamingWriteScope : IDisposable
    {
        public IndexOutput Output { get; }
        private readonly long _bodyStart;
        private bool _disposed;

        internal StreamingWriteScope(IndexOutput output, long bodyStart)
        {
            Output = output;
            _bodyStart = bodyStart;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            long bodyLen = Output.Position - _bodyStart;
            Output.WriteInt64(bodyLen);
        }
    }

    public static StreamingWriteScope BeginStreamingWrite(IndexOutput output, byte version)
    {
        output.WriteByte(version);
        long bodyStart = output.Position;
        return new StreamingWriteScope(output, bodyStart);
    }

    // -- Streaming reader (IndexInput) --------------------------------------

    /// <summary>
    /// Detects trailer vs envelope format, returns the version byte, and leaves
    /// <paramref name="input"/> positioned at the start of the body.
    /// </summary>
    public static byte ReadVersionAndSkipHeader(IndexInput input)
    {
        if (input.Length < 1)
            throw new InvalidDataException("CodecKit file is truncated.");

        long startPos = input.Position;
        byte version = input.ReadByte();
        long frameLen = input.Length - startPos;

        // Try trailer: last 8 bytes form a self-consistent bodyLen.
        if (frameLen >= 9)
        {
            input.Seek(input.Length - 8);
            long bodyLen = input.ReadInt64();
            if (1L + bodyLen + 8L == frameLen && bodyLen >= 0)
            {
                input.Seek(startPos + 1);
                return version;
            }
        }

        // Envelope: skip VarInt64 bodyLen.
        input.Seek(startPos + 1);
        SkipVarInt64(input);
        return version;
    }

    /// <summary>
    /// <c>BinaryReader</c> overload.  Envelope-only for now; trailer detection
    /// via BinaryReader is not safe due to internal buffering (addressed in
    /// Phase C when StoredFieldsReader handles v3).
    /// </summary>
    public static byte ReadVersionAndSkipHeader(BinaryReader reader)
    {
        byte version = reader.ReadByte();
        SkipVarInt64(reader);
        return version;
    }

    // -- Full-body materialising reads (IndexInput) -------------------------

    /// <summary>
    /// Detects format and returns the full body as a byte[].
    /// For envelope files the body codec may add its own framing;
    /// prefer <see cref="Read(IndexInput, ICodec{byte[]})"/> for those.
    /// </summary>
    public static ReadResult ReadBody(IndexInput input)
    {
        long startPos = input.Position;
        if (input.Length - startPos < 1)
            throw new InvalidDataException("CodecKit file is truncated.");

        byte version = input.ReadByte();
        long frameLen = input.Length - startPos;

        if (frameLen >= 9)
        {
            input.Seek(input.Length - 8);
            long bodyLen = input.ReadInt64();
            if (1L + bodyLen + 8L == frameLen && bodyLen >= 0)
            {
                byte[] body;
                if (bodyLen == 0)
                    body = [];
                else
                {
                    input.Seek(startPos + 1);
                    body = new byte[bodyLen];
                    for (long i = 0; i < bodyLen; i++)
                        body[i] = input.ReadByte();
                }
                return new ReadResult(body, version);
            }
        }

        // Envelope: read ZigZag-encoded VarInt64 bodyLen, then body.
        input.Seek(startPos + 1);
        long envBodyLen = ReadVarInt64(input);
        if (envBodyLen < 0 || envBodyLen > int.MaxValue)
            throw new InvalidDataException($"Invalid envelope body length: {envBodyLen}");
        byte[] envBody = new byte[(int)envBodyLen];
        for (int i = 0; i < (int)envBodyLen; i++)
            envBody[i] = input.ReadByte();
        return new ReadResult(envBody, version);
    }

    // ReadBody(BinaryReader) omitted; BinaryReader paths need a format
    // parameter for correct decode.  Use Read(BinaryReader, ICodec<byte[]>) instead.


    // -- Legacy Read / ReadVersion ------------------------------------------

    /// <summary>
    /// Reads and decodes a codec file.  Detects trailer format (raw body)
    /// and falls back to envelope format via the codec-chain decode path.
    /// </summary>
    public static ReadResult Read(IndexInput input, ICodec<byte[]> format)
    {
        if (input.Length - input.Position < 1)
            throw new InvalidDataException("CodecKit file is truncated: no payload bytes found.");

        long startPos = input.Position;
        long frameLen = input.Length - startPos;

        if (frameLen >= 9)
        {
            input.Seek(input.Length - 8);
            long bodyLen = input.ReadInt64();
            if (1L + bodyLen + 8L == frameLen && bodyLen >= 0)
            {
                byte version = input.ReadByte();
                byte[] body;
                if (bodyLen == 0)
                    body = [];
                else
                {
                    input.Seek(startPos + 1);
                    body = new byte[bodyLen];
                    for (long i = 0; i < bodyLen; i++)
                        body[i] = input.ReadByte();
                }
                return new ReadResult(body, version);
            }
        }

        // Envelope fallback: decode through the format chain.
        input.Seek(startPos);
        long remaining = input.Length - input.Position;
        byte[] raw = new byte[remaining];
        for (long i = 0; i < remaining; i++)
            raw[i] = input.ReadByte();

        var seq = new ReadOnlySequence<byte>(raw);
        var reader = new SequenceReader<byte>(seq);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);
        byte[] decodedBody;
        try
        {
            decodedBody = format.Decode(ref reader, ctx);
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException("CodecKit file is corrupt or truncated.", ex);
        }

        return new ReadResult(decodedBody, raw[0]);
    }

    public static byte ReadVersion(IndexInput input, ICodec<byte[]> format)
        => ReadVersionAndSkipHeader(input);

    public static byte ReadVersion(IndexInput input)
        => ReadVersionAndSkipHeader(input);

    public static ReadResult Read(BinaryReader reader, ICodec<byte[]> format)
    {
        var stream = reader.BaseStream;
        long remaining = stream.Length - stream.Position;
        if (remaining < 1)
            throw new InvalidDataException("CodecKit file is truncated: no payload bytes found.");
        byte[] raw = reader.ReadBytes((int)remaining);

        var seq = new ReadOnlySequence<byte>(raw);
        var seqReader = new SequenceReader<byte>(seq);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);
        byte[] decodedBody;
        try
        {
            decodedBody = format.Decode(ref seqReader, ctx);
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException("CodecKit file is corrupt or truncated.", ex);
        }

        return new ReadResult(decodedBody, raw[0]);
    }

    public static byte ReadVersion(BinaryReader reader, ICodec<byte[]> format)
        => ReadVersionAndSkipHeader(reader);

    public static byte ReadVersion(BinaryReader reader)
        => ReadVersionAndSkipHeader(reader);

    // -- Existing Write methods ---------------------------------------------

    public static void Write(IndexOutput output, ICodec<byte[]> format, byte[] body)
    {
        using var writer = new Adapters.IndexOutputBuffer(output);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);
        format.Encode(body, writer, ctx);
    }

    public static void Write(IndexOutput output, ICodec<byte[]> format, ReadOnlySpan<byte> body)
    {
        using var writer = new Adapters.IndexOutputBuffer(output);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);

        if (format is VersionEnvelopeCodec<byte[], byte> envelope)
            envelope.EncodeSpan(body, writer, ctx);
        else
            format.Encode(body.ToArray(), writer, ctx);
    }

    public static (T Value, byte Version) Read<T>(IndexInput input, ICodec<byte[]> format, ICodec<T> bodyCodec)
    {
        var result = Read(input, format);
        var seq = new ReadOnlySequence<byte>(result.Body);
        var reader = new SequenceReader<byte>(seq);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);
        T value = bodyCodec.Decode(ref reader, ctx);
        return (value, result.Version);
    }

    public static void Write(BinaryWriter writer, ICodec<byte[]> format, byte[] body)
    {
        var buf = new ArrayBufferWriter<byte>(body.Length + 16);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);
        format.Encode(body, buf, ctx);
        writer.Write(buf.WrittenSpan);
    }

    public static void Write(BinaryWriter writer, ICodec<byte[]> format, ReadOnlySpan<byte> body)
    {
        var buf = new ArrayBufferWriter<byte>(body.Length + 16);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);

        if (format is VersionEnvelopeCodec<byte[], byte> envelope)
            envelope.EncodeSpan(body, buf, ctx);
        else
            format.Encode(body.ToArray(), buf, ctx);

        writer.Write(buf.WrittenSpan);
    }

    // -- Private helpers ----------------------------------------------------

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

    /// <summary>Reads a ZigZag-encoded VarInt64 (Codec.VarInt64 wire format).</summary>
    private static long ReadVarInt64(IndexInput input)
    {
        ulong result = 0;
        int shift = 0;
        for (int i = 0; i < 10; i++)
        {
            byte b = input.ReadByte();
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                long signed = (long)(result >> 1);
                if ((result & 1) != 0) signed = ~signed;
                return signed;
            }
            shift += 7;
        }
        throw new InvalidDataException("VarInt64 body length is malformed (exceeds 10 bytes).");
    }

    private static long ReadVarInt64(BinaryReader reader)
    {
        ulong result = 0;
        int shift = 0;
        for (int i = 0; i < 10; i++)
        {
            byte b = reader.ReadByte();
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                long signed = (long)(result >> 1);
                if ((result & 1) != 0) signed = ~signed;
                return signed;
            }
            shift += 7;
        }
        throw new InvalidDataException("VarInt64 body length is malformed (exceeds 10 bytes).");
    }
}

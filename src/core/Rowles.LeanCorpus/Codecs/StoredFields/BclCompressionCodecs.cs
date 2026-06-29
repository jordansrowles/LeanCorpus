using System.IO.Compression;

namespace Rowles.LeanCorpus.Codecs.StoredFields;

internal sealed class NoneCompressionCodec : IFieldCompressionCodec
{
    public byte PolicyByte => (byte)FieldCompressionPolicy.None;

    public byte[] Compress(ReadOnlySpan<byte> raw)
    {
        var copy = new byte[raw.Length];
        raw.CopyTo(copy);
        return copy;
    }

    public byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(originalSize);

        if (compressed.Length != originalSize)
            throw new InvalidDataException($"Uncompressed stored fields contained {compressed.Length} bytes; expected {originalSize} bytes.");

        var raw = new byte[originalSize];
        compressed.CopyTo(raw);
        return raw;
    }
}

internal sealed class DeflateCompressionCodec : IBufferedFieldCompressionCodec
{
    public byte PolicyByte => (byte)FieldCompressionPolicy.Deflate;

    public byte[] Compress(ReadOnlySpan<byte> raw)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(raw);
        return CompressionBufferHelper.TrimBuffer(output);
    }

    public byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize)
    {
        using var input = new MemoryStream(compressed.Length);
        input.Write(compressed);
        input.Position = 0;
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        var raw = new byte[originalSize];
        deflate.ReadExactly(raw);
        return raw;
    }

    public (byte[] Data, int Length) CompressToBuffer(ReadOnlySpan<byte> raw)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(raw);

        return (output.GetBuffer(), (int)output.Length);
    }

    public byte[] Decompress(byte[] compressed, int compressedLength, int originalSize)
    {
        using var input = new MemoryStream(compressed, 0, compressedLength, writable: false);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        var raw = new byte[originalSize];
        deflate.ReadExactly(raw);
        return raw;
    }
}

internal sealed class BrotliCompressionCodec : IBufferedFieldCompressionCodec
{
    public byte PolicyByte => (byte)FieldCompressionPolicy.Brotli;

    public byte[] Compress(ReadOnlySpan<byte> raw)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
            brotli.Write(raw);
        return CompressionBufferHelper.TrimBuffer(output);
    }

    public byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize)
    {
        using var input = new MemoryStream(compressed.Length);
        input.Write(compressed);
        input.Position = 0;
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        var raw = new byte[originalSize];
        brotli.ReadExactly(raw);
        return raw;
    }

    public (byte[] Data, int Length) CompressToBuffer(ReadOnlySpan<byte> raw)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
            brotli.Write(raw);

        return (output.GetBuffer(), (int)output.Length);
    }

    public byte[] Decompress(byte[] compressed, int compressedLength, int originalSize)
    {
        using var input = new MemoryStream(compressed, 0, compressedLength, writable: false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        var raw = new byte[originalSize];
        brotli.ReadExactly(raw);
        return raw;
    }
}

/// <summary>
/// Returns a correctly-sized copy of a MemoryStream's internal buffer without the extra
/// allocation incurred by <see cref="MemoryStream.ToArray"/>.
/// </summary>
internal static class CompressionBufferHelper
{
    internal static byte[] TrimBuffer(MemoryStream stream)
    {
        int len = (int)stream.Length;
        byte[] buf = stream.GetBuffer();
        if (buf.Length == len)
            return buf;
        var result = new byte[len];
        Array.Copy(buf, result, len);
        return result;
    }
}

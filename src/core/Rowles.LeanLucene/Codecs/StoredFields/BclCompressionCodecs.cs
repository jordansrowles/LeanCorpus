using System.IO.Compression;

namespace Rowles.LeanLucene.Codecs.StoredFields;

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
        var raw = new byte[originalSize];
        compressed[..originalSize].CopyTo(raw);
        return raw;
    }
}

internal sealed class DeflateCompressionCodec : IBufferedFieldCompressionCodec
{
    public byte PolicyByte => (byte)FieldCompressionPolicy.Deflate;

    public byte[] Compress(ReadOnlySpan<byte> raw)
    {
        var (data, length) = CompressToBuffer(raw);
        return data.AsSpan(0, length).ToArray();
    }

    public byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize)
    {
        using var input = new MemoryStream(compressed.ToArray());
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
        var (data, length) = CompressToBuffer(raw);
        return data.AsSpan(0, length).ToArray();
    }

    public byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize)
    {
        using var input = new MemoryStream(compressed.ToArray());
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

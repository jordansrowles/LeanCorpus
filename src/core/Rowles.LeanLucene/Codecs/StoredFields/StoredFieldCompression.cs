namespace Rowles.LeanLucene.Codecs.StoredFields;

/// <summary>Compression/decompression dispatch for stored field blocks.</summary>
internal static class StoredFieldCompression
{
    /// <summary>Compresses raw block data using the specified policy.</summary>
    internal static (byte[] Data, int Length) Compress(ReadOnlySpan<byte> raw, FieldCompressionPolicy policy)
    {
        if (raw.Length == 0)
        {
            return ([], 0);
        }

        var codec = CompressionCodecRegistry.Get(policy);
        if (codec is IBufferedFieldCompressionCodec bufferedCodec)
        {
            return bufferedCodec.CompressToBuffer(raw);
        }

        byte[] compressed = codec.Compress(raw);
        return (compressed, compressed.Length);
    }

    /// <summary>Decompresses block data using the specified policy.</summary>
    internal static byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize, FieldCompressionPolicy policy)
    {
        if (originalSize == 0)
        {
            return [];
        }

        if (policy == FieldCompressionPolicy.None)
        {
            var raw = new byte[originalSize];
            compressed[..originalSize].CopyTo(raw);
            return raw;
        }

        return CompressionCodecRegistry.Get(policy).Decompress(compressed, originalSize);
    }

    /// <summary>Decompresses block data from an array-backed buffer using the specified policy.</summary>
    internal static byte[] Decompress(byte[] compressed, int compressedLength, int originalSize, FieldCompressionPolicy policy)
    {
        if (originalSize == 0)
        {
            return [];
        }

        if (policy == FieldCompressionPolicy.None)
        {
            var raw = new byte[originalSize];
            compressed.AsSpan(0, originalSize).CopyTo(raw);
            return raw;
        }

        var codec = CompressionCodecRegistry.Get(policy);
        if (codec is IBufferedFieldCompressionCodec bufferedCodec)
        {
            return bufferedCodec.Decompress(compressed, compressedLength, originalSize);
        }

        return codec.Decompress(compressed.AsSpan(0, compressedLength), originalSize);
    }
}

internal interface IBufferedFieldCompressionCodec : IFieldCompressionCodec
{
    (byte[] Data, int Length) CompressToBuffer(ReadOnlySpan<byte> raw);

    byte[] Decompress(byte[] compressed, int compressedLength, int originalSize);
}

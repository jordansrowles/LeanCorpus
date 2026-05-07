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

        byte[] compressed = CompressionCodecRegistry.Get(policy).Compress(raw);
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
}

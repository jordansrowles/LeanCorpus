namespace Rowles.LeanCorpus.Codecs.StoredFields;

using Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>Compression/decompression dispatch for stored field blocks.</summary>
internal static class StoredFieldCompression
{
    /// <summary>Compresses raw block data using the specified policy.</summary>
    internal static (byte[] Data, int Length) Compress(ReadOnlySpan<byte> raw, FieldCompressionPolicy policy)
        => Compress(raw, CodecCatalog.Default.GetCompressionCodec((byte)policy));

    internal static (byte[] Data, int Length) Compress(ReadOnlySpan<byte> raw, IFieldCompressionCodec codec)
    {
        if (raw.Length == 0)
        {
            return ([], 0);
        }

        if (codec is IBufferedFieldCompressionCodec bufferedCodec)
        {
            return bufferedCodec.CompressToBuffer(raw);
        }

        byte[] compressed = codec.Compress(raw);
        return (compressed, compressed.Length);
    }

    /// <summary>Decompresses block data using the specified policy.</summary>
    internal static byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize, FieldCompressionPolicy policy)
        => Decompress(compressed, originalSize, CodecCatalog.Default.GetCompressionCodec((byte)policy));

    internal static byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize, IFieldCompressionCodec codec)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(originalSize);

        if (originalSize == 0)
        {
            if (!compressed.IsEmpty)
                throw new InvalidDataException("Stored fields compressed data for an empty payload must also be empty.");

            return [];
        }

        return codec.Decompress(compressed, originalSize);
    }

    /// <summary>Decompresses block data from an array-backed buffer using the specified policy.</summary>
    internal static byte[] Decompress(byte[] compressed, int compressedLength, int originalSize, FieldCompressionPolicy policy)
        => Decompress(
            compressed,
            compressedLength,
            originalSize,
            CodecCatalog.Default.GetCompressionCodec((byte)policy));

    internal static byte[] Decompress(
        byte[] compressed,
        int compressedLength,
        int originalSize,
        IFieldCompressionCodec codec)
    {
        ArgumentNullException.ThrowIfNull(compressed);
        ArgumentOutOfRangeException.ThrowIfNegative(originalSize);

        if ((uint)compressedLength > (uint)compressed.Length)
            throw new ArgumentOutOfRangeException(nameof(compressedLength));

        if (originalSize == 0)
        {
            if (compressedLength != 0)
                throw new InvalidDataException("Stored fields compressed data for an empty payload must also be empty.");

            return [];
        }

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

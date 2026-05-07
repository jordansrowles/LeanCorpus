namespace Rowles.LeanLucene.Codecs.StoredFields;

/// <summary>
/// Provides compression and decompression for stored field blocks.
/// </summary>
public interface IFieldCompressionCodec
{
    /// <summary>
    /// Gets the persisted stored-field compression policy byte handled by this codec.
    /// </summary>
    byte PolicyByte { get; }

    /// <summary>
    /// Compresses raw stored-field block data.
    /// </summary>
    /// <param name="raw">The uncompressed block data.</param>
    /// <returns>The compressed block data.</returns>
    byte[] Compress(ReadOnlySpan<byte> raw);

    /// <summary>
    /// Decompresses stored-field block data.
    /// </summary>
    /// <param name="compressed">The compressed block data.</param>
    /// <param name="originalSize">The expected number of bytes in the decompressed block.</param>
    /// <returns>The decompressed block data.</returns>
    byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize);
}

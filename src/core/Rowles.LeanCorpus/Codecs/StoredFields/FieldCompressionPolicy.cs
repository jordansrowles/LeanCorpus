namespace Rowles.LeanCorpus.Codecs.StoredFields;

/// <summary>
/// Compression algorithm for stored fields.
/// </summary>
public enum FieldCompressionPolicy : byte
{
    /// <summary>No compression. Fastest writes, largest on-disc size.</summary>
    None = 0,

    /// <summary>LZ4 compression provided by the optional LZ4 compression package.</summary>
    Lz4 = 1,

    /// <summary>Zstandard compression provided by the optional Zstandard compression package.</summary>
    Zstandard = 2,

    /// <summary>Brotli compression (legacy, for reading old segments only).</summary>
    Brotli = 3,

    /// <summary>Deflate compression provided by the base class library.</summary>
    Deflate = 4,

    /// <summary>Snappy compression provided by the optional Snappy compression package.</summary>
    Snappy = 5
}

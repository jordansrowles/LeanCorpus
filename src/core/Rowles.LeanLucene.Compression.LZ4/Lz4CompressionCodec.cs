using System.Runtime.CompilerServices;
using K4os.Compression.LZ4;
using Rowles.LeanLucene.Codecs.StoredFields;

namespace Rowles.LeanLucene.Compression.LZ4;

/// <summary>
/// Provides the LZ4 stored-field compression codec.
/// </summary>
public sealed class Lz4CompressionCodec : IFieldCompressionCodec
{
    /// <inheritdoc />
    public byte PolicyByte => (byte)FieldCompressionPolicy.Lz4;

    /// <inheritdoc />
    public byte[] Compress(ReadOnlySpan<byte> raw)
    {
        if (raw.Length == 0)
            return [];

        var output = new byte[LZ4Codec.MaximumOutputSize(raw.Length)];
        int written = LZ4Codec.Encode(raw, output, LZ4Level.L00_FAST);
        if (written <= 0)
            throw new InvalidDataException("LZ4 compression failed.");

        Array.Resize(ref output, written);
        return output;
    }

    /// <inheritdoc />
    public byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize)
    {
        if (originalSize == 0)
            return [];

        var raw = new byte[originalSize];
        int decoded = LZ4Codec.Decode(compressed, raw);
        if (decoded != originalSize)
            throw new InvalidDataException($"LZ4 decompressed {decoded} bytes; expected {originalSize} bytes.");

        return raw;
    }
}

/// <summary>
/// Registers the LZ4 stored-field compression codec.
/// </summary>
public static class Lz4Compression
{
    /// <summary>
    /// Registers the LZ4 codec with the LeanLucene compression codec registry.
    /// </summary>
    /// <remarks>
    /// In standard .NET applications a <see cref="System.Runtime.CompilerServices.ModuleInitializerAttribute"/>
    /// calls this method automatically when the assembly is loaded. Native AOT consumers
    /// must call this method explicitly at startup; the IL trimmer may eliminate the
    /// module initialiser if no types from this assembly are directly referenced in code.
    /// </remarks>
    public static void Register()
    {
        CompressionCodecRegistry.Register(new Lz4CompressionCodec());
    }

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialise()
    {
        Register();
    }
}

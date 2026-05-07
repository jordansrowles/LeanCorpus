using System.Runtime.CompilerServices;
using Rowles.LeanLucene.Codecs.StoredFields;
using ZstdSharp;

namespace Rowles.LeanLucene.Compression.Zstandard;

/// <summary>
/// Provides the Zstandard stored-field compression codec.
/// </summary>
public sealed class ZstandardCompressionCodec : IFieldCompressionCodec
{
    /// <inheritdoc />
    public byte PolicyByte => (byte)FieldCompressionPolicy.Zstandard;

    /// <inheritdoc />
    public byte[] Compress(ReadOnlySpan<byte> raw)
    {
        using var compressor = new Compressor();
        return compressor.Wrap(raw).ToArray();
    }

    /// <inheritdoc />
    public byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize)
    {
        using var decompressor = new Decompressor();
        var raw = decompressor.Unwrap(compressed, originalSize).ToArray();
        if (raw.Length != originalSize)
            throw new InvalidDataException($"Zstandard decompressed {raw.Length} bytes; expected {originalSize} bytes.");

        return raw;
    }
}

/// <summary>
/// Registers the Zstandard stored-field compression codec.
/// </summary>
public static class ZstandardCompression
{
    /// <summary>
    /// Registers the Zstandard codec with the LeanLucene compression codec registry.
    /// </summary>
    /// <remarks>
    /// In standard .NET applications a <see cref="System.Runtime.CompilerServices.ModuleInitializerAttribute"/>
    /// calls this method automatically when the assembly is loaded. Native AOT consumers
    /// must call this method explicitly at startup; the IL trimmer may eliminate the
    /// module initialiser if no types from this assembly are directly referenced in code.
    /// </remarks>
    public static void Register()
    {
        CompressionCodecRegistry.Register(new ZstandardCompressionCodec());
    }

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialise()
    {
        Register();
    }
}

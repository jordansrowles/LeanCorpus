using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Codecs.StoredFields;

namespace Rowles.LeanCorpus.Compression.Snappy;

/// <summary>
/// Provides the Snappy stored-field compression codec.
/// </summary>
public sealed class SnappyCompressionCodec : IFieldCompressionCodec
{
    /// <inheritdoc />
    public byte PolicyByte => (byte)FieldCompressionPolicy.Snappy;

    /// <inheritdoc />
    public byte[] Compress(ReadOnlySpan<byte> raw)
    {
        return global::Snappier.Snappy.CompressToArray(raw);
    }

    /// <inheritdoc />
    public byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize)
    {
        var raw = global::Snappier.Snappy.DecompressToArray(compressed);
        if (raw.Length != originalSize)
            throw new InvalidDataException($"Snappy decompressed {raw.Length} bytes; expected {originalSize} bytes.");

        return raw;
    }
}

/// <summary>
/// Registers the Snappy stored-field compression codec.
/// </summary>
public static class SnappyCompression
{
    /// <summary>
    /// Registers the Snappy codec with the LeanCorpus compression codec registry.
    /// </summary>
    /// <remarks>
    /// In standard .NET applications a <see cref="System.Runtime.CompilerServices.ModuleInitializerAttribute"/>
    /// calls this method automatically when the assembly is loaded. Native AOT consumers
    /// must call this method explicitly at startup; the IL trimmer may eliminate the
    /// module initialiser if no types from this assembly are directly referenced in code.
    /// </remarks>
    public static void Register()
    {
        CompressionCodecRegistry.Register(new SnappyCompressionCodec());
    }

#pragma warning disable CA2255
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(SnappyCompressionCodec))]
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialise()
    {
        Register();
    }
}

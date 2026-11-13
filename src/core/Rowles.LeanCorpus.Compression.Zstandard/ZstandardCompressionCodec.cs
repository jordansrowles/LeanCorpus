using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Codecs.StoredFields;
using ZstdSharp;

namespace Rowles.LeanCorpus.Compression.Zstandard;

/// <summary>
/// Provides the Zstandard stored-field compression codec.
/// </summary>
public sealed class ZstandardCompressionCodec : IFieldCompressionCodec
{
    private static readonly ConcurrentBag<Compressor> Compressors = [];
    private static readonly ConcurrentBag<Decompressor> Decompressors = [];

    /// <inheritdoc />
    public byte PolicyByte => (byte)FieldCompressionPolicy.Zstandard;

    /// <inheritdoc />
    public byte[] Compress(ReadOnlySpan<byte> raw)
    {
        var compressor = RentCompressor();
        try
        {
            return compressor.Wrap(raw).ToArray();
        }
        finally
        {
            Compressors.Add(compressor);
        }
    }

    /// <inheritdoc />
    public byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize)
    {
        if (originalSize < 0)
            throw new InvalidDataException($"Zstandard original size {originalSize} is negative.");

        var decompressor = RentDecompressor();
        try
        {
            // Decode straight into the returned buffer. A frame that decodes to more bytes
            // than originalSize fails inside Unwrap; one that decodes to fewer fails below.
            var raw = new byte[originalSize];
            int written = decompressor.Unwrap(compressed, raw);
            if (written != originalSize)
                throw new InvalidDataException($"Zstandard decompressed {written} bytes; expected {originalSize} bytes.");

            return raw;
        }
        finally
        {
            Decompressors.Add(decompressor);
        }
    }

    private static Compressor RentCompressor()
    {
        if (Compressors.TryTake(out var compressor))
            return compressor;

        return new Compressor();
    }

    private static Decompressor RentDecompressor()
    {
        if (Decompressors.TryTake(out var decompressor))
            return decompressor;

        return new Decompressor();
    }
}

/// <summary>
/// Registers the Zstandard stored-field compression codec.
/// </summary>
public static class ZstandardCompression
{
    private static readonly object RegistrationSyncRoot = new();
    private static bool _registered;

    /// <summary>
    /// Registers the Zstandard codec with the LeanCorpus compression codec registry.
    /// </summary>
    /// <remarks>
    /// In standard .NET applications a <see cref="System.Runtime.CompilerServices.ModuleInitializerAttribute"/>
    /// calls this method automatically when the assembly is loaded. Native AOT consumers
    /// must call this method explicitly at startup; the IL trimmer may eliminate the
    /// module initialiser if no types from this assembly are directly referenced in code.
    /// </remarks>
    public static void Register()
    {
        lock (RegistrationSyncRoot)
        {
            if (_registered)
                return;
            CompressionCodecRegistry.Register(new ZstandardCompressionCodec());
            _registered = true;
        }
    }

#pragma warning disable CA2255
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ZstandardCompressionCodec))]
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialise()
    {
        Register();
    }
}

using System;
using System.Collections.Generic;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

/// <summary>
/// Immutable registry of checksum providers.
/// Use the builder pattern to register providers, then build an immutable instance.
/// </summary>
public sealed class CodecRegistry
{
    private readonly Dictionary<ChecksumAlgorithmId, IChecksumProvider> _checksumProviders;

    private CodecRegistry(
        Dictionary<ChecksumAlgorithmId, IChecksumProvider> checksumProviders)
    {
        _checksumProviders = checksumProviders;
    }

    /// <summary>Creates an empty registry for building up from scratch.</summary>
    public static CodecRegistry Empty { get; } = new(
        new Dictionary<ChecksumAlgorithmId, IChecksumProvider>());

    /// <summary>
    /// Default registry pre-loaded with CRC32, xxHash32, and xxHash64 checksum providers.
    /// </summary>
    public static CodecRegistry Default { get; } = Empty
        .WithChecksum(ChecksumAlgorithms.Crc32, new Crc32Provider())
        .WithChecksum(ChecksumAlgorithms.XxHash32, new XxHash32Provider())
        .WithChecksum(ChecksumAlgorithms.XxHash64, new XxHash64Provider());

    /// <summary>Looks up a checksum provider by algorithm ID.</summary>
    public IChecksumProvider GetChecksumProvider(ChecksumAlgorithmId algorithmId)
    {
        if (_checksumProviders.TryGetValue(algorithmId, out var provider))
            return provider;
        throw new CodecValidationException(CodecErrorCode.UnknownAlgorithm, 0, string.Empty,
            $"No checksum provider registered for algorithm '{algorithmId.Name}'.");
    }

    /// <summary>Returns a new registry with an additional checksum provider.</summary>
    public CodecRegistry WithChecksum(ChecksumAlgorithmId algorithmId, IChecksumProvider provider)
    {
        var newDict = new Dictionary<ChecksumAlgorithmId, IChecksumProvider>(_checksumProviders)
        {
            [algorithmId] = provider
        };
        return new CodecRegistry(newDict);
    }

}

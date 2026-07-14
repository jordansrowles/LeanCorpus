using System;
using System.Buffers;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;

/// <summary>
/// Provides checksum computation and verification over byte sequences.
/// Implementations must be thread-safe and stateless.
/// </summary>
public interface IChecksumProvider
{
    /// <summary>Size of the checksum in bytes.</summary>
    int ChecksumByteLength { get; }

    /// <summary>
    /// Computes the checksum of the given data.
    /// Returns a byte array of exactly <see cref="ChecksumByteLength"/> bytes.
    /// </summary>
    byte[] Compute(ReadOnlySequence<byte> data);

    /// <summary>
    /// Verifies the checksum of the given data against the expected checksum.
    /// </summary>
    bool Verify(ReadOnlySequence<byte> data, ReadOnlySpan<byte> expectedChecksum);
}

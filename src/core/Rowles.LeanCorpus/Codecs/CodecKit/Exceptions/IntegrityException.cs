using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

/// <summary>A checksum verification failed.</summary>
public sealed class ChecksumMismatchException : CodecIntegrityException
{
    public ChecksumMismatchException(long byteOffset, string path, string algorithmId, byte[] expected, byte[] actual)
        : base(CodecErrorCode.ChecksumMismatch, byteOffset, path,
            $"Checksum mismatch ({algorithmId}) at offset {byteOffset}. Path: {path}")
    {
        AlgorithmId = algorithmId;
        ExpectedChecksum = expected;
        ActualChecksum = actual;
    }

    public string AlgorithmId { get; }
    public byte[] ExpectedChecksum { get; }
    public byte[] ActualChecksum { get; }
}

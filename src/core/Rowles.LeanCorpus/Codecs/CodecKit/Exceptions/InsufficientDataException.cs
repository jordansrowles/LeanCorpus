using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

/// <summary>Not enough bytes available to decode the expected value.</summary>
public sealed class InsufficientDataException : CodecFormatException
{
    public InsufficientDataException(long byteOffset, string path, int expectedBytes, int availableBytes)
        : base(CodecErrorCode.Truncated, byteOffset, path,
            $"Insufficient data at offset {byteOffset}: expected {expectedBytes} bytes but only {availableBytes} available. Path: {path}")
    {
        ExpectedBytes = expectedBytes;
        AvailableBytes = availableBytes;
    }

    public int ExpectedBytes { get; }
    public int AvailableBytes { get; }
}

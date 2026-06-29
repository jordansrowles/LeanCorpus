using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

/// <summary>Encoded payload exceeds a fixed frame size.</summary>
public sealed class FrameOverflowException : CodecResourceException
{
    public FrameOverflowException(long byteOffset, string path, int frameSize, int payloadSize)
        : base(CodecErrorCode.FrameOverflow, byteOffset, path,
            $"Frame size mismatch at offset {byteOffset}: payload is {payloadSize} bytes but frame requires {frameSize}. Path: {path}")
    {
        FrameSize = frameSize;
        PayloadSize = payloadSize;
    }

    public int FrameSize { get; }
    public int PayloadSize { get; }
}

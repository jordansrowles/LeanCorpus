using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

/// <summary>A configured limit was exceeded (e.g., MaxFrameBytes, MaxSequenceElements).</summary>
public sealed class LimitExceededException : CodecResourceException
{
    public LimitExceededException(CodecErrorCode errorCode, long byteOffset, string path, string limitName, long actualValue, long limitValue)
        : base(errorCode, byteOffset, path,
            $"Limit exceeded at offset {byteOffset}: {limitName} was {actualValue}, maximum is {limitValue}. Path: {path}")
    {
        LimitName = limitName;
        ActualValue = actualValue;
        LimitValue = limitValue;
    }

    public LimitExceededException(CodecErrorCode errorCode, long byteOffset, string path, string limitName, long actualValue, string limitDescription, Exception? innerException = null)
        : base(errorCode, byteOffset, path,
            $"Limit exceeded at offset {byteOffset}: {limitName} was {actualValue}, maximum is {limitDescription}. Path: {path}", innerException)
    {
        LimitName = limitName;
        ActualValue = actualValue;
        LimitValue = -1;
    }

    public string LimitName { get; }
    public long ActualValue { get; }
    public long LimitValue { get; }
}

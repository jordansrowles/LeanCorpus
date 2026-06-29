using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

/// <summary>A user-supplied delegate (e.g., Map, Record.Build) threw an exception.</summary>
public sealed class UserCodeException : CodecException
{
    public UserCodeException(long byteOffset, string path, Exception innerException)
        : base(CodecErrorCode.UserCodeFailed, byteOffset, path,
            $"User code failed at offset {byteOffset}: {innerException.Message}. Path: {path}",
            innerException)
    {
    }
}

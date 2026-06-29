using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

/// <summary>A configured resource limit was exceeded: frame overflow, nesting depth, scratch buffer size, etc.</summary>
public class CodecResourceException : CodecException
{
    public CodecResourceException(CodecErrorCode errorCode, long byteOffset, string path, string message, Exception? innerException = null)
        : base(errorCode, byteOffset, path, message, innerException)
    {
    }
}

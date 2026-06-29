using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

/// <summary>Data integrity check failed (e.g. checksum mismatch).</summary>
public class CodecIntegrityException : CodecException
{
    public CodecIntegrityException(CodecErrorCode errorCode, long byteOffset, string path, string message, Exception? innerException = null)
        : base(errorCode, byteOffset, path, message, innerException)
    {
    }
}

using System;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
namespace Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

/// <summary>
/// Base class for all codec-related exceptions.
/// Carries structured diagnostic information: byte offset, path, and machine-readable error code.
/// </summary>
public abstract class CodecException : Exception
{
    protected CodecException(CodecErrorCode errorCode, long byteOffset, string path, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        ByteOffset = byteOffset;
        Path = path;
    }

    /// <summary>Machine-readable error code for programmatic handling.</summary>
    public CodecErrorCode ErrorCode { get; }

    /// <summary>Absolute byte offset where the error was detected.</summary>
    public long ByteOffset { get; }

    /// <summary>Diagnostic breadcrumb path (e.g., "Header > Cells[3] > Value").</summary>
    public string Path { get; }
}

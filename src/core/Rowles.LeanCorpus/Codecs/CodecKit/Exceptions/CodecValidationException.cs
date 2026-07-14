using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
﻿using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

/// <summary>A <c>Validate</c> predicate returned false, or an invalid value was decoded (wrong magic, bad padding, invalid boolean/UTF-8).</summary>
public class CodecValidationException : CodecException
{
    public CodecValidationException(CodecErrorCode errorCode, long byteOffset, string path, string message, Exception? innerException = null)
        : base(errorCode, byteOffset, path, message, innerException)
    {
    }

    public CodecValidationException(long byteOffset, string path, string validationMessage)
        : this(CodecErrorCode.ValidationFailed, byteOffset, path,
            $"Validation failed at offset {byteOffset}: {validationMessage}. Path: {path}")
    {
        ValidationMessage = validationMessage;
    }

    public string? ValidationMessage { get; }
}

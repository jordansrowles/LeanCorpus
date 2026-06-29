using System;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

/// <summary>
/// Represents the result of a non-throwing codec operation.
/// Access <see cref="Value"/> on success or <see cref="Failure"/> on failure.
/// Accessing the wrong property throws <see cref="InvalidOperationException"/>.
/// </summary>
public readonly struct CodecResult<T>
{
    private readonly T? _value;
    private readonly CodecFailure? _failure;
    private readonly bool _isSuccess;

    private CodecResult(T value)
    {
        _value = value;
        _failure = null;
        _isSuccess = true;
    }

    private CodecResult(CodecFailure failure)
    {
        _value = default;
        _failure = failure;
        _isSuccess = false;
    }

    /// <summary>True if the operation succeeded.</summary>
    public bool IsSuccess => _isSuccess;

    /// <summary>True if the operation failed.</summary>
    public bool IsFailure => !_isSuccess;

    /// <summary>
    /// The decoded/encoded value. Throws <see cref="InvalidOperationException"/> if the operation failed.
    /// </summary>
    public T Value => _isSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot access Value on a failed CodecResult. Failure: {_failure!.Message}");

    /// <summary>
    /// The failure metadata. Throws <see cref="InvalidOperationException"/> if the operation succeeded.
    /// </summary>
    public CodecFailure Failure => !_isSuccess
        ? _failure!
        : throw new InvalidOperationException("Cannot access Failure on a successful CodecResult.");

    /// <summary>Creates a successful result.</summary>
    public static CodecResult<T> Success(T value) => new(value);

    /// <summary>Creates a failed result from a <see cref="CodecFailure"/>.</summary>
    public static CodecResult<T> Fail(CodecFailure failure) => new(failure);

    /// <summary>Creates a failed result from a <see cref="CodecException"/>.</summary>
    public static CodecResult<T> Fail(CodecException exception) =>
        new(new CodecFailure(exception.ErrorCode, exception.ByteOffset, exception.Path, exception.Message, exception));
}

using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Enums;

/// <summary>
/// Machine-readable error codes for all codec failure categories.
/// Every <see cref="Exceptions.CodecException"/> and <see cref="CodecFailure"/> carries one of these values.
/// </summary>
public enum CodecErrorCode
{
    Truncated,
    Overflow,
    InvalidBoolean,
    InvalidUtf8,
    InvalidValue,
    MagicMismatch,
    InvalidPadding,
    TrailingData,
    UnknownDiscriminator,
    UnknownVersion,
    ChecksumMismatch,
    FrameOverflow,
    FrameTooLarge,
    SequenceTooLarge,
    InvalidScope,
    ValidationFailed,
    UserCodeFailed,
    DecompressionFailed,
    DepthExceeded,
    NegativeLength,
    DuplicateFieldName,
    UnknownAlgorithm,
    DecompressionLimitExceeded,
}

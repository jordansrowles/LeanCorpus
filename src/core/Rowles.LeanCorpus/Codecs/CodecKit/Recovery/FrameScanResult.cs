using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using System.Diagnostics.CodeAnalysis;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Recovery;

/// <summary>
/// Represents the result of attempting to decode a single frame during a recovery scan.
/// </summary>
internal readonly struct FrameScanResult<T>
{
    /// <summary>Byte offset in the source where this decode attempt began.</summary>
    public long Offset { get; }

    /// <summary>Whether the decode succeeded.</summary>
#if NET8_0_OR_GREATER
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Failure))]
#endif
    public bool Success { get; }

    /// <summary>The decoded value when <see cref="Success"/> is <c>true</c>.</summary>
    public T? Value { get; }

    /// <summary>Failure metadata when <see cref="Success"/> is <c>false</c>.</summary>
    public CodecFailure? Failure { get; }

    internal FrameScanResult(long offset, T value)
    {
        Offset = offset;
        Success = true;
        Value = value;
        Failure = null;
    }

    internal FrameScanResult(long offset, CodecFailure failure)
    {
        Offset = offset;
        Success = false;
        Value = default;
        Failure = failure;
    }
}

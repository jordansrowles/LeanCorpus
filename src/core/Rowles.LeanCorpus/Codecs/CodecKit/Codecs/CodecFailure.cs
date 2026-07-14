using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

/// <summary>
/// Structured failure metadata for the non-throwing <c>TryDecode</c>/<c>TryEncode</c> paths.
/// </summary>
public sealed record CodecFailure(
    CodecErrorCode Code,
    long ByteOffset,
    string Path,
    string Message,
    Exception? InnerException = null);

using System;
using System.Text;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// Shared UTF-8 validation helper used by all UTF-8 codecs.
/// </summary>
internal static class Utf8Helpers
{
    /// <summary>
    /// Returns <c>true</c> if the given byte span is well-formed UTF-8.
    /// Uses the optimised runtime API on .NET 8+ and falls back to
    /// a throwing <see cref="UTF8Encoding"/> on older targets.
    /// </summary>
    public static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
#if NET8_0_OR_GREATER
        return System.Text.Unicode.Utf8.IsValid(bytes);
#else
        try
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            encoding.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
#endif
    }
}

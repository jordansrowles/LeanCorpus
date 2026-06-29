namespace Rowles.LeanCorpus.Codecs.CodecKit.Enums;

/// <summary>
/// Controls how UTF-8 byte sequences are validated during string decoding.
/// </summary>
public enum Utf8ValidationMode
{
    /// <summary>Invalid UTF-8 throws <see cref="Exceptions.InvalidUtf8Exception"/>.</summary>
    Strict = 0,

    /// <summary>Invalid UTF-8 bytes are replaced with U+FFFD.</summary>
    Replace = 1,
}

namespace Rowles.LeanCorpus.Codecs.CodecKit.Enums;

/// <summary>
/// Controls whether trailing bytes after the expected payload are rejected or silently allowed.
/// </summary>
public enum TrailingDataPolicy
{
    /// <summary>Trailing bytes cause a <see cref="Exceptions.TrailingDataException"/>.</summary>
    Reject = 0,

    /// <summary>Trailing bytes are silently ignored.</summary>
    Allow = 1,
}

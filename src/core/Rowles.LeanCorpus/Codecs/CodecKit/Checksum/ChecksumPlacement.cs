namespace Rowles.LeanCorpus.Codecs.CodecKit.Checksum;

/// <summary>
/// Controls where the checksum is placed relative to the body.
/// </summary>
public enum ChecksumPlacement
{
    /// <summary>Checksum is placed before the body: <c>[checksum][body]</c>.</summary>
    Header = 0,

    /// <summary>Checksum is placed after the body: <c>[body][checksum]</c>.</summary>
    Trailer = 1,
}

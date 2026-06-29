namespace Rowles.LeanCorpus.Codecs.CodecKit.Checksum;

/// <summary>
/// Well-known checksum algorithm identifiers.
/// </summary>
internal static class ChecksumAlgorithms
{
    public static ChecksumAlgorithmId Crc32 { get; } = new("crc32");
    public static ChecksumAlgorithmId XxHash32 { get; } = new("xxhash32");
    public static ChecksumAlgorithmId XxHash64 { get; } = new("xxhash64");
}

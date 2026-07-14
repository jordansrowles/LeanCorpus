using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Checksum;

/// <summary>
/// Identifies a checksum algorithm in the <see cref="CodecRegistry"/>.
/// </summary>
public sealed record ChecksumAlgorithmId(string Name)
{
    public override string ToString() => Name;
}

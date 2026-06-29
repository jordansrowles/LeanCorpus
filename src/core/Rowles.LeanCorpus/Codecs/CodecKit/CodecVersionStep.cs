using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>
/// One version's reader in a codec format version chain.
/// All readers in a format produce the same model type (<c>byte[]</c>),
/// supplying defaults for fields added after their version.
/// </summary>
/// <param name="Version">The integer version number this step handles.</param>
/// <param name="Label">Diagnostic label for path segments.</param>
/// <param name="Reader">The reader codec for this version's data.</param>
public sealed record CodecVersionStep(
    int Version,
    string Label,
    ICodec<byte[]> Reader);

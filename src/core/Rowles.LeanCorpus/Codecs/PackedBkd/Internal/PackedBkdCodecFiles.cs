using Rowles.LeanCorpus.Codecs.CodecKit;
namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

internal static class PackedBkdCodecFiles
{
    internal static CodecFileDescriptor Descriptor { get; } =
        CodecCatalog.Default.GetFile("leancorpus.numeric-structures.packed-bkd");
}

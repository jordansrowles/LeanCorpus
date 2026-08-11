using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Codecs.Vectors;

internal static class VectorCodecFiles
{
    internal static CodecFileDescriptor Float32 { get; } =
        CodecCatalog.Default.GetFile("leancorpus.vectors.float32");

    internal static CodecFileDescriptor Quantised { get; } =
        CodecCatalog.Default.GetFile("leancorpus.vectors.quantised");

    internal static CodecFileDescriptor Hnsw { get; } =
        CodecCatalog.Default.GetFile("leancorpus.vectors.hnsw");
}

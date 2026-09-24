using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Codecs.ShapeDocValues;

internal static class ShapeDocValuesCodecFiles
{
    internal static CodecFileDescriptor Data { get; } =
        CodecCatalog.Default.GetFile("leancorpus.doc-values.shape");
}

using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Codecs.DocValues;

internal static class DocValuesCodecFiles
{
    internal static CodecFileDescriptor Numeric { get; } =
        CodecCatalog.Default.GetFile("leancorpus.doc-values.numeric");

    internal static CodecFileDescriptor Sorted { get; } =
        CodecCatalog.Default.GetFile("leancorpus.doc-values.sorted");

    internal static CodecFileDescriptor SortedSet { get; } =
        CodecCatalog.Default.GetFile("leancorpus.doc-values.sorted-set");

    internal static CodecFileDescriptor SortedNumeric { get; } =
        CodecCatalog.Default.GetFile("leancorpus.doc-values.sorted-numeric");

    internal static CodecFileDescriptor Binary { get; } =
        CodecCatalog.Default.GetFile("leancorpus.doc-values.binary");

    internal static CodecFileDescriptor Int64 { get; } =
        CodecCatalog.Default.GetFile("leancorpus.doc-values.int64");

    internal static CodecFileDescriptor Int64SortedNumeric { get; } =
        CodecCatalog.Default.GetFile("leancorpus.doc-values.int64-sorted-numeric");
}

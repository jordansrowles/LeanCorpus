using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>Checks the required logical members of a segment without decoding codec bodies.</summary>
internal static class SegmentStructureValidator
{
    private static readonly string[] RequiredCodecExtensions = [".dic", ".pos", ".nrm", ".fdt", ".fdx"];

    internal static void ValidateRequiredFiles(SegmentDescriptor info, ISegmentFileSource source)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(source);

        RequireNonEmptyFile(source, info.SegmentId + ".seg");
        if (info.IsCompoundFile)
            RequireNonEmptyFile(source, info.SegmentId + ".cfs");

        foreach (string extension in RequiredCodecExtensions)
            RequireNonEmptyFile(source, info.SegmentId + extension);

        foreach (VectorFieldInfo vector in info.VectorFields)
        {
            string vectorFileName = Path.GetFileName(vector.Quantisation == VectorQuantisation.None
                ? VectorFilePaths.VectorFile(info.SegmentId, vector.FieldName)
                : VectorFilePaths.QuantisedVectorFile(info.SegmentId, vector.FieldName));
            RequirePresentFile(source, vectorFileName, $"Segment '{info.SegmentId}' is missing vector file '{vectorFileName}'.");

            if (vector.HasHnsw)
            {
                string hnswFileName = Path.GetFileName(VectorFilePaths.HnswFile(info.SegmentId, vector.FieldName));
                RequirePresentFile(source, hnswFileName, $"Segment '{info.SegmentId}' is missing HNSW file '{hnswFileName}'.");
            }
        }

        DeletionStateValidator.RequireFileIfSelected(info, source.EnumerateFiles());
    }

    private static void RequireNonEmptyFile(ISegmentFileSource source, string fileName)
    {
        RequirePresentFile(source, fileName, $"Segment file is missing: '{fileName}'.");
        if (source.GetFileLength(fileName) <= 0)
            throw new InvalidDataException($"Required segment file is empty: '{fileName}'.");
    }

    private static void RequirePresentFile(ISegmentFileSource source, string fileName, string message)
    {
        if (!source.FileExists(fileName))
            throw new FileNotFoundException(message, fileName);
    }
}

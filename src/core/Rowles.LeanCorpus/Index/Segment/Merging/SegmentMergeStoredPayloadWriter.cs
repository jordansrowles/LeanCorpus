using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.TermVectors;

namespace Rowles.LeanCorpus.Index.Segment.Merging;

/// <summary>Owns per-document stored payload streaming.</summary>
internal static class SegmentMergeStoredPayloadWriter
{
    internal static void Write(SegmentMergePlan plan, CodecCatalog catalog,
        SegmentMergeDocValuesWriter.State columns, SegmentMergeVectorWriter.State vectors,
        SegmentMergeSpatialWriter.State spatial)
    {
        bool anyTermVectors = plan.Readers.Values.Any(reader => reader.HasTermVectors);
        using var storedWriter = new StoredFieldsStreamWriter(
            plan.BasePath + ".fdt", plan.BasePath + ".fdx", catalog: catalog);
        using var termVectorWriter = anyTermVectors
            ? new TermVectorsStreamWriter(plan.BasePath + ".tvd", plan.BasePath + ".tvx") : null;
        foreach (var source in plan.DocumentMap.Sources)
        {
            SegmentMergeSpatialWriter.AccumulateSource(source, spatial);
            SegmentMergeDocValuesWriter.AccumulateSource(source, columns);
            SegmentMergeVectorWriter.AccumulateSource(source, vectors);
        }
        for (int newDocId = 0; newDocId < plan.TotalDocs; newDocId++)
        {
            var document = plan.DocumentMap.Documents[newDocId];
            storedWriter.AddDocument(document.Reader.GetStoredFieldValues(document.OldDocId));
            if (termVectorWriter is not null)
                termVectorWriter.AddDocument(document.Reader.HasTermVectors
                    ? document.Reader.GetTermVectors(document.OldDocId) : null);
            SegmentMergeSpatialWriter.CopyDocument(document, newDocId, spatial);
        }
    }
}



namespace Rowles.LeanCorpus.Index.Segment.Merging;

/// <summary>Shared source-to-destination mapping; negative entries omit source documents.</summary>
internal sealed class SegmentMergeDocumentMap
{
    internal readonly record struct Source(SegmentInfo Seg, int[] DocIdMap, SegmentReader Reader);
    internal readonly record struct MergeDocument(SegmentInfo Segment, SegmentReader Reader, int OldDocId);
    internal IReadOnlyList<Source> Sources { get; }
    internal IReadOnlyList<MergeDocument> Documents { get; }
    internal int Count => Documents.Count;

    internal SegmentMergeDocumentMap(List<Source> sources, int totalDocs)
    {
        Sources = sources.AsReadOnly();
        Documents = Array.AsReadOnly(BuildDestinationDocumentOrder(Sources, totalDocs));
    }

    internal static MergeDocument[] BuildDestinationDocumentOrder(
        IReadOnlyList<SegmentMergeDocumentMap.Source> perSegmentMaps,
        int totalDocs)
    {
        var documentOrder = new MergeDocument[totalDocs];
        foreach ((SegmentInfo segment, int[] docIdMap, SegmentReader reader) in perSegmentMaps)
        {
            for (int oldDocId = 0; oldDocId < docIdMap.Length; oldDocId++)
            {
                int newDocId = docIdMap[oldDocId];
                if (newDocId < 0)
                    continue;
                if ((uint)newDocId >= (uint)documentOrder.Length || documentOrder[newDocId].Segment is not null)
                    throw new InvalidDataException("The merge document remap contains duplicate or out-of-range destination IDs.");

                documentOrder[newDocId] = new MergeDocument(segment, reader, oldDocId);
            }
        }

        for (int newDocId = 0; newDocId < documentOrder.Length; newDocId++)
            if (documentOrder[newDocId].Segment is null)
                throw new InvalidDataException("The merge document remap contains a gap in destination IDs.");

        return documentOrder;
    }

}

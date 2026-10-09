using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Index.Segment.Merging;

/// <summary>Owns streaming postings and dictionary output.</summary>
internal static class SegmentMergePostingsWriter
{
    internal static void Write(SegmentMergePlan plan, CodecCatalog catalog)
        => MergePostings(plan.DocumentMap.Sources, plan.BasePath, catalog);

    private static void MergePostings(
        IReadOnlyList<SegmentMergeDocumentMap.Source> sources,
        string basePath, CodecCatalog catalog)
    {
        var merger = new List<StreamingPostingsMerger.Source>(sources.Count);
        foreach (var (_, map, reader) in sources)
        {
            merger.Add(new StreamingPostingsMerger.Source
            {
                OpenInput = reader.OpenInput,
                DocIdMap = map,
            });
        }
        StreamingPostingsMerger.Merge(merger, basePath + ".pos", basePath + ".dic", catalog);
    }

}

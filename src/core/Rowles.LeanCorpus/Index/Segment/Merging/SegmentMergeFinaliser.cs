using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Index.Indexer;

namespace Rowles.LeanCorpus.Index.Segment.Merging;

/// <summary>Owns destination preparation and registered-file cleanup.</summary>
internal static class SegmentMergeFinaliser
{
    internal static SegmentInfo Finish(SegmentMergePlan plan, List<VectorFieldInfo> mergedVectorFields,
        MMapDirectory directory, CodecCatalog catalog, bool useCompoundFile)
    {
        LiveDocs? mergedLiveDocs = null;
        if (plan.SoftDeletes.Count > 0)
        {
            mergedLiveDocs = new LiveDocs(plan.TotalDocs);
            foreach (var (docId, timestamp) in plan.SoftDeletes)
                mergedLiveDocs.SoftDelete(docId, timestamp);
            LiveDocs.Serialise(plan.BasePath + ".del", mergedLiveDocs);
        }

        var mergedInfo = new SegmentInfo
        {
            SegmentId = plan.SegmentId,
            DocCount = plan.TotalDocs,
            LiveDocCount = mergedLiveDocs?.LiveCount ?? plan.TotalDocs,
            CommitGeneration = plan.CommitGeneration,
            FieldNames = plan.FieldNames.ToList(),
            IndexSortFields = plan.IndexSortFields?.ToList(),
            VectorFields = mergedVectorFields,
            SpatialFields = plan.SpatialFields.ToList(),
            MinSequenceNumber = plan.MinimumSequenceNumber,
            MaxSequenceNumber = plan.MaximumSequenceNumber,
            EarliestSoftDeleteTimestamp = mergedLiveDocs?.EarliestSoftDeleteTimestamp,
        };
        if (useCompoundFile && SegmentFileSet.Pack(directory.DirectoryPath, plan.SegmentId, catalog))
            mergedInfo.IsCompoundFile = true;
        SegmentFlusher.RefreshSegmentSize(mergedInfo, directory.DirectoryPath, catalog);

        SegmentStats mergedStats;
        using (var statisticsReader = new SegmentReader(directory, mergedInfo, catalog))
            mergedStats = SegmentStats.FromSegmentReader(statisticsReader);
        mergedStats.WriteTo(SegmentStats.GetStatsPath(directory.DirectoryPath, plan.SegmentId));

        return mergedInfo;
    }

    internal static void Cleanup(MMapDirectory directory, string segmentId, CodecCatalog catalog)
    {
        try
        {
            SegmentFileSet.Enumerate(directory.DirectoryPath, segmentId, catalog)
                .DeleteAllOwnedFiles(directory, "failed merge destination cleanup");
        }
        catch (Exception exception)
        {
            Diagnostics.LeanCorpusActivitySource.TraceSwallowed(exception, "failed merge destination inventory");
        }
    }
}

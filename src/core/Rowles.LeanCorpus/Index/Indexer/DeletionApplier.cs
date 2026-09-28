using System.Diagnostics;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Applies pending deletions to segment live-docs bitmaps and the writer's in-memory commit state.
/// All methods are static and operate on parameters only, with no coupling back to <see cref="IndexWriter"/>.
/// </summary>
internal static class DeletionApplier
{
    public static void ApplyPendingDeletions(
        PendingDeleteQueue deleteQueue,
        List<SegmentInfo> segments,
        MMapDirectory directory,
        int commitGeneration,
        bool durableCommits,
        Diagnostics.IMetricsCollector metrics,
        CodecCatalog codecCatalog)
    {
        var stopwatch = Stopwatch.StartNew();
        var pendingDeletes = deleteQueue.GetOrderedList();
        int deleteTermCount = pendingDeletes.Count;
        int changedSegments = 0;
        _ = durableCommits;
        using var activity = Diagnostics.LeanCorpusActivitySource.Source
            .StartActivity(Diagnostics.LeanCorpusActivitySource.DeleteApply);
        if (pendingDeletes.Count == 0) return;

        // Group by ordinal: hard and soft deletes separated
        var hardTermsByOrdinal = new Dictionary<int, List<DeleteTerm>>();
        var softTermsByOrdinal = new Dictionary<int, List<DeleteTerm>>();
        long softDeleteTimestamp = 0;

        foreach (var dt in pendingDeletes)
        {
            var dict = dt.IsSoftDelete ? softTermsByOrdinal : hardTermsByOrdinal;
            if (!dict.TryGetValue(dt.FieldOrdinal, out var list))
            {
                list = [];
                dict[dt.FieldOrdinal] = list;
            }
            list.Add(dt);
        }

        if (softTermsByOrdinal.Count > 0)
            softDeleteTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        int pendingGen = commitGeneration + 1;
        var dirPath = directory.DirectoryPath;

        foreach (var seg in segments)
        {
            var basePath = Path.Combine(dirPath, seg.SegmentId);
            using var segmentReader = new SegmentReader(directory, seg, codecCatalog);
            if (!segmentReader.FileExists(".dic") || !segmentReader.FileExists(".pos"))
                continue;

            using var dicReader = TermDictionaryReader.Open(segmentReader.OpenInput(".dic"));

            var liveDocs = DeletionStateValidator.RequireValid(basePath, seg)
                ?? new LiveDocs(seg.DocCount);

            bool changed = false;
            using var posInput = segmentReader.OpenInput(".pos");
            byte postingsVersion = PostingsEnum.ValidateFileHeader(posInput);

            ApplyDeletesByOrdinal(dicReader, posInput, postingsVersion, liveDocs,
                hardTermsByOrdinal, softDelete: false, 0, ref changed);
            ApplyDeletesByOrdinal(dicReader, posInput, postingsVersion, liveDocs,
                softTermsByOrdinal, softDelete: true, softDeleteTimestamp, ref changed);

            if (changed)
            {
                changedSegments++;
                var newDelPath = basePath + $"_gen_{pendingGen}.del";
                LiveDocs.Serialise(newDelPath, liveDocs, durable: false);
                seg.DelGeneration = pendingGen;
                seg.LiveDocCount = liveDocs.LiveCount;
                seg.EarliestSoftDeleteTimestamp = liveDocs.EarliestSoftDeleteTimestamp;
                UpdateSegmentStatistics(basePath, seg, segmentReader, liveDocs);
            }
        }

        deleteQueue.Clear();
        stopwatch.Stop();
        metrics.RecordDeleteApplication(stopwatch.Elapsed, deleteTermCount, changedSegments);
    }

    private static void ReadPostingsAtOffsetInto(
        IndexInput input, long offset, byte postingsVersion, LiveDocs liveDocs,
        ref bool changed, bool softDelete = false,
        long softDeleteTimestamp = 0)
    {
        using var pe = PostingsEnum.Create(input, offset);
        while (pe.MoveNext())
        {
            int docId = pe.DocId;
            if (liveDocs.IsLive(docId))
            {
                if (softDelete)
                    liveDocs.SoftDelete(docId, softDeleteTimestamp);
                else
                    liveDocs.Delete(docId);
                changed = true;
            }
        }
    }

    private static void ApplyDeletesByOrdinal(
        TermDictionaryReader dicReader, IndexInput posInput, byte postingsVersion,
        LiveDocs liveDocs, Dictionary<int, List<DeleteTerm>> termsByOrdinal,
        bool softDelete, long softDeleteTimestamp, ref bool changed)
    {
        foreach (var (_, deleteTerms) in termsByOrdinal)
        {
            foreach (var dt in deleteTerms)
            {
                var qualifiedBytes = dt.BuildQualifiedTermBytes();
                if (!dicReader.TryGetPostingsOffset(qualifiedBytes, out long offset))
                    continue;

                ReadPostingsAtOffsetInto(posInput, offset, postingsVersion,
                    liveDocs, ref changed, softDelete, softDeleteTimestamp);
            }
        }
    }

    private static void UpdateSegmentStatistics(
        string basePath,
        SegmentInfo segment,
        SegmentReader reader,
        LiveDocs liveDocs)
    {
        var statsPath = SegmentStats.GetStatsPath(
            Path.GetDirectoryName(basePath)!, segment.SegmentId, segment.DelGeneration);
        SegmentStats.FromSegmentReader(reader, liveDocs.IsLive).WriteTo(statsPath);
    }
}

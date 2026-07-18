using System.Diagnostics;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Applies pending deletions to segment live-docs bitmaps.
/// All methods are static — operates on parameters only, no coupling back to <see cref="IndexWriter"/>.
/// </summary>
internal static class DeletionApplier
{
    public static void ApplyPendingDeletions(
        List<DeleteTerm> pendingDeletes,
        List<SegmentInfo> segments,
        MMapDirectory directory,
        int commitGeneration,
        bool durableCommits,
        Diagnostics.IMetricsCollector metrics)
    {
        var stopwatch = Stopwatch.StartNew();
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
            var dicPath = basePath + ".dic";
            var posPath = basePath + ".pos";

            if (!FileOpenRetry.FileExists(dicPath) || !FileOpenRetry.FileExists(posPath))
                continue;

            using var dicReader = TermDictionaryReader.Open(dicPath);

            string existingDelPath = seg.DelGeneration.HasValue
                ? basePath + $"_gen_{seg.DelGeneration.Value}.del"
                : basePath + ".del";

            var liveDocs = FileOpenRetry.FileExists(existingDelPath)
                ? LiveDocs.Deserialise(existingDelPath, seg.DocCount)
                : new LiveDocs(seg.DocCount);

            bool changed = false;
            var newlyDeleted = new HashSet<int>();
            using var posInput = new IndexInput(posPath);
            byte postingsVersion = PostingsEnum.ValidateFileHeader(posInput);

            ApplyDeletesByOrdinal(dicReader, posInput, postingsVersion, liveDocs,
                hardTermsByOrdinal, newlyDeleted, softDelete: false, 0, ref changed);
            ApplyDeletesByOrdinal(dicReader, posInput, postingsVersion, liveDocs,
                softTermsByOrdinal, newlyDeleted, softDelete: true, softDeleteTimestamp, ref changed);

            if (changed)
            {
                changedSegments++;
                var newDelPath = basePath + $"_gen_{pendingGen}.del";
                LiveDocs.Serialise(newDelPath, liveDocs, durable: false);
                seg.DelGeneration = pendingGen;
                seg.LiveDocCount = liveDocs.LiveCount;
                seg.EarliestSoftDeleteTimestamp = liveDocs.EarliestSoftDeleteTimestamp;
                UpdateSegmentStatistics(basePath, seg, newlyDeleted);
                seg.WriteTo(basePath + ".seg");
            }
        }

        pendingDeletes.Clear();
        stopwatch.Stop();
        metrics.RecordDeleteApplication(stopwatch.Elapsed, deleteTermCount, changedSegments);
    }

    private static void ReadPostingsAtOffsetInto(
        IndexInput input, long offset, byte postingsVersion, LiveDocs liveDocs,
        HashSet<int> newlyDeleted, ref bool changed, bool softDelete = false,
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
                newlyDeleted.Add(docId);
                changed = true;
            }
        }
    }

    private static void ApplyDeletesByOrdinal(
        TermDictionaryReader dicReader, IndexInput posInput, byte postingsVersion,
        LiveDocs liveDocs, Dictionary<int, List<DeleteTerm>> termsByOrdinal,
        HashSet<int> newlyDeleted, bool softDelete, long softDeleteTimestamp, ref bool changed)
    {
        foreach (var (_, deleteTerms) in termsByOrdinal)
        {
            foreach (var dt in deleteTerms)
            {
                var qualifiedBytes = dt.BuildQualifiedTermBytes();
                if (!dicReader.TryGetPostingsOffset(qualifiedBytes, out long offset))
                    continue;

                ReadPostingsAtOffsetInto(posInput, offset, postingsVersion,
                    liveDocs, newlyDeleted, ref changed, softDelete, softDeleteTimestamp);
            }
        }
    }

    private static void UpdateSegmentStatistics(string basePath, SegmentInfo segment, HashSet<int> deletedDocIds)
    {
        var statsPath = SegmentStats.GetStatsPath(Path.GetDirectoryName(basePath)!, segment.SegmentId);
        var existing = SegmentStats.TryLoadFrom(statsPath);
        if (existing is null)
            return;

        var sums = new Dictionary<string, long>(existing.FieldLengthSums, StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(existing.FieldDocCounts, StringComparer.Ordinal);
        var lengths = FieldLengthReader.TryRead(basePath + ".fln");

        foreach (int docId in deletedDocIds)
        {
            foreach (string field in counts.Keys.ToArray())
            {
                long contribution = 1;
                if (lengths is not null && lengths.TryGetValue(field, out var fieldLengths) &&
                    (uint)docId < (uint)fieldLengths.Length)
                {
                    contribution = fieldLengths[docId];
                }
                sums[field] = Math.Max(0, sums.GetValueOrDefault(field) - contribution);
                counts[field] = Math.Max(0, counts[field] - 1);
            }
        }

        new SegmentStats(segment.DocCount, segment.LiveDocCount, sums, counts).WriteTo(statsPath);
    }
}

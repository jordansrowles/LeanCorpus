using System.Diagnostics;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Applies pending deletions to segment live-docs bitmaps.
/// All methods are static — operates on parameters only, no coupling back to <see cref="IndexWriter"/>.
/// </summary>
internal static class DeletionApplier
{
    public static void ApplyPendingDeletions(
        List<(string field, string term, bool isSoftDelete)> pendingDeletes,
        List<SegmentInfo> segments,
        MMapDirectory directory,
        int commitGeneration,
        bool durableCommits)
    {
        using var activity = Diagnostics.LeanCorpusActivitySource.Source
            .StartActivity(Diagnostics.LeanCorpusActivitySource.DeleteApply);
        if (pendingDeletes.Count == 0) return;

        var hardDeleteTermsByField = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var softDeleteTermsByField = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        long softDeleteTimestamp = 0;

        foreach (var (field, term, isSoftDelete) in pendingDeletes)
        {
            var dict = isSoftDelete ? softDeleteTermsByField : hardDeleteTermsByField;
            if (!dict.TryGetValue(field, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                dict[field] = set;
            }
            set.Add(term);
        }

        if (softDeleteTermsByField.Count > 0)
            softDeleteTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        int pendingGen = commitGeneration + 1;
        var dirPath = directory.DirectoryPath;

        // Pre-compute doc-ID ranges for segment routing of "id"-field deletes.
        int docBase = 0;
        var segmentRanges = new (int Min, int Max, SegmentInfo Seg)[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            segmentRanges[i] = (docBase, docBase + segments[i].DocCount - 1, segments[i]);
            docBase += segments[i].DocCount;
        }

        foreach (var (min, max, seg) in segmentRanges)
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
            using var posInput = new IndexInput(posPath);
            byte postingsVersion = PostingsEnum.ValidateFileHeader(posInput);

            // Route "id"-field terms to only the segment containing their doc ID.
            var routedHard = RouteDeletesBySegment(hardDeleteTermsByField, min, max);
            var routedSoft = RouteDeletesBySegment(softDeleteTermsByField, min, max);

            ApplyDeletesByField(dicReader, posInput, postingsVersion, liveDocs,
                routedHard, softDelete: false, 0, ref changed);
            ApplyDeletesByField(dicReader, posInput, postingsVersion, liveDocs,
                routedSoft, softDelete: true, softDeleteTimestamp, ref changed);

            if (changed)
            {
                var newDelPath = basePath + $"_gen_{pendingGen}.del";
                LiveDocs.Serialise(newDelPath, liveDocs, durableCommits);
                seg.DelGeneration = pendingGen;
                seg.LiveDocCount = liveDocs.LiveCount;
                seg.EarliestSoftDeleteTimestamp = liveDocs.EarliestSoftDeleteTimestamp;
                seg.WriteTo(basePath + ".seg");
            }
        }

        pendingDeletes.Clear();
    }

    private static void ReadPostingsAtOffsetInto(
        IndexInput input, long offset, byte postingsVersion, LiveDocs liveDocs,
        ref bool changed, bool softDelete = false, long softDeleteTimestamp = 0)
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

    private static void ApplyDeletesByField(
        TermDictionaryReader dicReader, IndexInput posInput, byte postingsVersion,
        LiveDocs liveDocs, Dictionary<string, HashSet<string>> termsByField,
        bool softDelete, long softDeleteTimestamp, ref bool changed)
    {
        Span<char> stackBuf = stackalloc char[256];

        foreach (var (field, terms) in termsByField)
        {
            foreach (var term in terms)
            {
                int fieldLen = field.Length;
                int termLen = term.Length;
                int totalLen = fieldLen + 1 + termLen;
                Span<char> qualified = totalLen <= 256
                    ? stackBuf.Slice(0, totalLen)
                    : new char[totalLen];
                field.AsSpan().CopyTo(qualified);
                qualified[fieldLen] = '\0';
                term.AsSpan().CopyTo(qualified.Slice(fieldLen + 1));

                if (!dicReader.TryGetPostingsOffset(qualified, out long offset))
                    continue;

                ReadPostingsAtOffsetInto(posInput, offset, postingsVersion,
                    liveDocs, ref changed, softDelete, softDeleteTimestamp);
            }
        }
    }

    /// <summary>Routes delete terms to the segment containing their doc ID.
    /// Terms in the "id" field that parse as integers are filtered to the matching range;
    /// all other terms pass through to every segment unchanged.</summary>
    private static Dictionary<string, HashSet<string>> RouteDeletesBySegment(
        Dictionary<string, HashSet<string>> termsByField,
        int minDocId, int maxDocId)
    {
        var routed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (field, terms) in termsByField)
        {
            if (field == "id")
            {
                var inRange = new HashSet<string>(StringComparer.Ordinal);
                foreach (var term in terms)
                {
                    if (int.TryParse(term, System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out int docId))
                    {
                        if (docId >= minDocId && docId <= maxDocId)
                            inRange.Add(term);
                    }
                    else
                    {
                        // Non-numeric id terms cannot be routed; pass through to all segments.
                        inRange.Add(term);
                    }
                }
                if (inRange.Count > 0)
                    routed[field] = inRange;
            }
            else
            {
                routed[field] = terms;
            }
        }
        return routed;
    }
}

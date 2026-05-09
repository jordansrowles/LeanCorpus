using System.Buffers;
namespace Rowles.LeanLucene.Search.Searcher;

/// <summary>
/// Partial class containing phrase query execution and position-based scoring logic.
/// </summary>
public sealed partial class IndexSearcher
{
    private void ExecutePhraseQuery(PhraseQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        if (query.Terms.Length == 0) return;

        int termCount = query.Terms.Length;
        var qualifiedTerms = query.QualifiedTerms;

        // Open position-aware PostingsEnums for all terms
        Span<PostingsEnum> postingsArr = new PostingsEnum[termCount];
        for (int i = 0; i < termCount; i++)
        {
            postingsArr[i] = reader.GetPostingsEnumWithPositions(qualifiedTerms[i]);
            if (postingsArr[i].IsExhausted)
            {
                for (int j = 0; j <= i; j++)
                    postingsArr[j].Dispose();
                return;
            }
        }

        // Find leader (rarest term) for efficient intersection
        int leaderIdx = 0;
        for (int i = 1; i < termCount; i++)
        {
            if (postingsArr[i].DocFreq < postingsArr[leaderIdx].DocFreq)
                leaderIdx = i;
        }

        int docBase = reader.DocBase;
        float boost = query.Boost;
        float score = boost != 1.0f ? boost : 1.0f;
        int slop = query.Slop;

        // Streaming merge: iterate leader, advance followers
        while (postingsArr[leaderIdx].MoveNext())
        {
            int docId = postingsArr[leaderIdx].DocId;
            if (!reader.IsLive(docId)) continue;

            bool allMatch = true;
            for (int i = 0; i < termCount; i++)
            {
                if (i == leaderIdx) continue;
                if (!postingsArr[i].Advance(docId) || postingsArr[i].DocId != docId)
                {
                    allMatch = false;
                    break;
                }
            }

            if (!allMatch) continue;

            // All terms present in this doc — check positions inline
            bool hasAllPositions = true;
            for (int i = 0; i < termCount; i++)
            {
                if (postingsArr[i].GetCurrentPositions().IsEmpty)
                {
                    hasAllPositions = false;
                    break;
                }
            }

            if (hasAllPositions && HasPositionsWithinSlopSpan(postingsArr, termCount, leaderIdx, slop))
            {
                collector.Collect(docBase + docId, score);
            }
            // No fallback to stored fields — positions are required for phrase matching
        }

        for (int i = 0; i < termCount; i++)
            postingsArr[i].Dispose();
    }

    /// <summary>
    /// Checks whether positions from all terms form a valid phrase within the given slop,
    /// using ReadOnlySpan positions from PostingsEnum.
    /// </summary>
    private static bool HasPositionsWithinSlopSpan(Span<PostingsEnum> postings, int termCount, int leaderIdx, int slop)
    {
        if (termCount == 1) return true;

        // For 2 terms (common case): O(n+m) two-pointer merge on sorted positions
        if (termCount == 2)
        {
            var pos0 = postings[0].GetCurrentPositions();
            var pos1 = postings[1].GetCurrentPositions();
            int j = 0;
            for (int i = 0; i < pos0.Length; i++)
            {
                int target = pos0[i] + 1;
                int lowerBound = target - slop;
                int upperBound = target + slop;
                while (j < pos1.Length && pos1[j] < lowerBound)
                    j++;
                if (j >= pos1.Length) break;
                if (pos1[j] <= upperBound)
                    return true;
            }
            return false;
        }

        // 3-term specialisation: direct span access, zero allocation (matches 2-term path)
        if (termCount == 3)
        {
            var pos0 = postings[0].GetCurrentPositions();
            var pos1 = postings[1].GetCurrentPositions();
            var pos2 = postings[2].GetCurrentPositions();
            int j = 0, k = 0;
            for (int i = 0; i < pos0.Length; i++)
            {
                int target1 = pos0[i] + 1;
                int lo1 = target1 - slop;
                int hi1 = target1 + slop;
                while (j < pos1.Length && pos1[j] < lo1)
                    j++;
                if (j >= pos1.Length) break;
                if (pos1[j] > hi1) continue;

                int target2 = pos1[j] + 1;
                int lo2 = target2 - slop;
                int hi2 = target2 + slop;
                while (k < pos2.Length && pos2[k] < lo2)
                    k++;
                if (k >= pos2.Length) break;
                if (pos2[k] <= hi2)
                    return true;
            }
            return false;
        }

        // General N-term case (4+ terms): chained two-pointer with ArrayPool
        var rentedArrays = new int[termCount][];
        Span<int> lengths = stackalloc int[termCount];
        Span<int> cursors = stackalloc int[termCount];

        try
        {
            for (int i = 0; i < termCount; i++)
            {
                var span = postings[i].GetCurrentPositions();
                if (span.IsEmpty) return false;
                var rented = ArrayPool<int>.Shared.Rent(span.Length);
                span.CopyTo(rented);
                rentedArrays[i] = rented;
                lengths[i] = span.Length;
                cursors[i] = 0;
            }

            // Drive on the first term's positions; chain-advance subsequent terms
            int[] p0 = rentedArrays[0];
            int p0Len = lengths[0];

            for (int i = 0; i < p0Len; i++)
            {
                int chainPos = p0[i];
                bool matched = true;

                for (int t = 1; t < termCount; t++)
                {
                    int target = chainPos + 1;
                    int lo = target - slop;
                    int hi = target + slop;
                    int[] pt = rentedArrays[t];
                    int ptLen = lengths[t];
                    ref int cursor = ref cursors[t];

                    // Advance cursor past positions below the lower bound
                    while (cursor < ptLen && pt[cursor] < lo)
                        cursor++;

                    if (cursor >= ptLen) { matched = false; break; }
                    if (pt[cursor] > hi) { matched = false; break; }

                    // Chain: next term's target is relative to where we matched
                    chainPos = pt[cursor];
                }

                if (matched) return true;
            }

            return false;
        }
        finally
        {
            for (int i = 0; i < termCount; i++)
            {
                if (rentedArrays[i] != null)
                    ArrayPool<int>.Shared.Return(rentedArrays[i]);
            }
        }
    }
}

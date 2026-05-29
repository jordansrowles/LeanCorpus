using System.Buffers;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Partial class containing phrase query execution and position-based scoring logic.
/// </summary>
public sealed partial class IndexSearcher
{
    private void ExecutePhraseQuery(PhraseQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        if (query.Terms.Length == 0) return;
        ExecutePhraseQueryWithPositionEnums(query, reader, ref collector);
    }

    private void ExecutePhraseQueryWithPositionEnums(PhraseQuery query, SegmentReader reader, ref TopNCollector collector)
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
        reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);
        bool hasDeletions = reader.HasDeletions;

        // Streaming merge: iterate leader, advance followers
        while (postingsArr[leaderIdx].MoveNext())
        {
            int docId = postingsArr[leaderIdx].DocId;
            if (hasDeletions && !reader.IsLive(docId)) continue;

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
                collector.Collect(docBase + docId, ApplyFieldBoost(fieldBoosts, docId, score));
            }
            // No fallback to stored fields — positions are required for phrase matching
        }

        for (int i = 0; i < termCount; i++)
            postingsArr[i].Dispose();
    }

    private void ExecuteMultiPhraseQuery(MultiPhraseQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        if (query.TermGroups.Count == 0)
            return;
        if (TryExecuteTwoSlotMultiPhraseQuery(query, reader, ref collector))
            return;

        var perGroupPositions = new List<Dictionary<int, int[]>>(query.TermGroups.Count);
        HashSet<int>? candidateDocs = null;

        foreach (var group in query.TermGroups)
        {
            var positionsByDoc = new Dictionary<int, List<int>>();
            foreach (var term in group)
            {
                using var postings = reader.GetPostingsEnumWithPositions(string.Concat(query.Field, "\x00", term));
                while (postings.MoveNext())
                {
                    int docId = postings.DocId;
                    if (!reader.IsLive(docId))
                        continue;

                    var positions = postings.GetCurrentPositions();
                    if (positions.IsEmpty)
                        continue;

                    if (!positionsByDoc.TryGetValue(docId, out var docPositions))
                    {
                        docPositions = [];
                        positionsByDoc[docId] = docPositions;
                    }

                    for (int i = 0; i < positions.Length; i++)
                        docPositions.Add(positions[i]);
                }
            }

            if (positionsByDoc.Count == 0)
                return;

            var compacted = new Dictionary<int, int[]>(positionsByDoc.Count);
            foreach (var (docId, docPositions) in positionsByDoc)
            {
                docPositions.Sort();
                compacted[docId] = docPositions.Distinct().ToArray();
            }

            candidateDocs ??= new HashSet<int>(compacted.Keys);
            candidateDocs.IntersectWith(compacted.Keys);
            if (candidateDocs.Count == 0)
                return;

            perGroupPositions.Add(compacted);
        }

        int[] expectedPositions = query.Positions.ToArray();
        int docBase = reader.DocBase;
        float baseScore = query.Boost != 1.0f ? query.Boost : 1.0f;

        foreach (int docId in candidateDocs!)
        {
            var slotPositions = new int[perGroupPositions.Count][];
            for (int i = 0; i < perGroupPositions.Count; i++)
                slotPositions[i] = perGroupPositions[i][docId];

            if (HasMultiPhrasePositionsWithinSlop(slotPositions, expectedPositions, query.Slop))
                collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, baseScore));
        }
    }

    private bool TryExecuteTwoSlotMultiPhraseQuery(MultiPhraseQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        if (query.TermGroups.Count != 2 || query.Positions.Count != 2)
            return false;

        var group0 = query.TermGroups[0];
        var group1 = query.TermGroups[1];
        var postings0 = new PostingsEnum[group0.Count];
        var postings1 = new PostingsEnum[group1.Count];
        var docs0 = new int[group0.Count];
        var docs1 = new int[group1.Count];
        var positions0 = new List<int>();
        var positions1 = new List<int>();

        try
        {
            if (!InitialiseMultiPhraseGroup(query.Field, group0, reader, postings0, docs0) ||
                !InitialiseMultiPhraseGroup(query.Field, group1, reader, postings1, docs1))
            {
                return true;
            }

            int docBase = reader.DocBase;
            int expectedDelta = query.Positions[1] - query.Positions[0];
            int slop = query.Slop;
            float score = query.Boost != 1.0f ? query.Boost : 1.0f;
            reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);
            bool hasDeletions = reader.HasDeletions;

            int doc0 = GetCurrentGroupDoc(docs0);
            int doc1 = GetCurrentGroupDoc(docs1);
            while (doc0 != int.MaxValue && doc1 != int.MaxValue)
            {
                if (doc0 < doc1)
                {
                    AdvanceMultiPhraseGroup(postings0, docs0, doc0);
                    doc0 = GetCurrentGroupDoc(docs0);
                    continue;
                }

                if (doc1 < doc0)
                {
                    AdvanceMultiPhraseGroup(postings1, docs1, doc1);
                    doc1 = GetCurrentGroupDoc(docs1);
                    continue;
                }

                int docId = doc0;
                if (!hasDeletions || reader.IsLive(docId))
                {
                    CollectMultiPhraseGroupPositions(postings0, docs0, docId, positions0);
                    CollectMultiPhraseGroupPositions(postings1, docs1, docId, positions1);
                    if (HasTwoSlotMultiPhrasePositionsWithinSlop(positions0, positions1, expectedDelta, slop))
                        collector.Collect(docBase + docId, ApplyFieldBoost(fieldBoosts, docId, score));
                }

                AdvanceMultiPhraseGroup(postings0, docs0, docId);
                AdvanceMultiPhraseGroup(postings1, docs1, docId);
                doc0 = GetCurrentGroupDoc(docs0);
                doc1 = GetCurrentGroupDoc(docs1);
            }

            return true;
        }
        finally
        {
            DisposePostings(postings0);
            DisposePostings(postings1);
        }
    }

    private static bool InitialiseMultiPhraseGroup(string field, IReadOnlyList<string> terms, SegmentReader reader,
        PostingsEnum[] postings, int[] currentDocs)
    {
        bool anyTermHasDocs = false;
        for (int i = 0; i < terms.Count; i++)
        {
            postings[i] = reader.GetPostingsEnumWithPositions(string.Concat(field, "\x00", terms[i]));
            if (postings[i].MoveNext())
            {
                currentDocs[i] = postings[i].DocId;
                anyTermHasDocs = true;
            }
            else
            {
                currentDocs[i] = int.MaxValue;
            }
        }

        return anyTermHasDocs;
    }

    private static int GetCurrentGroupDoc(int[] currentDocs)
    {
        int docId = int.MaxValue;
        for (int i = 0; i < currentDocs.Length; i++)
        {
            if (currentDocs[i] < docId)
                docId = currentDocs[i];
        }

        return docId;
    }

    private static void AdvanceMultiPhraseGroup(PostingsEnum[] postings, int[] currentDocs, int docId)
    {
        for (int i = 0; i < postings.Length; i++)
        {
            if (currentDocs[i] == docId)
                currentDocs[i] = postings[i].MoveNext() ? postings[i].DocId : int.MaxValue;
        }
    }

    private static void CollectMultiPhraseGroupPositions(PostingsEnum[] postings, int[] currentDocs, int docId, List<int> positions)
    {
        positions.Clear();
        for (int i = 0; i < postings.Length; i++)
        {
            if (currentDocs[i] != docId)
                continue;

            var currentPositions = postings[i].GetCurrentPositions();
            for (int j = 0; j < currentPositions.Length; j++)
                positions.Add(currentPositions[j]);
        }

        positions.Sort();
    }

    private static void DisposePostings(PostingsEnum[] postings)
    {
        for (int i = 0; i < postings.Length; i++)
            postings[i].Dispose();
    }

    private static bool HasTwoSlotMultiPhrasePositionsWithinSlop(List<int> positions0, List<int> positions1,
        int expectedDelta, int slop)
    {
        int j = 0;
        int? previous0 = null;
        for (int i = 0; i < positions0.Count; i++)
        {
            int pos0 = positions0[i];
            if (previous0 == pos0)
                continue;
            previous0 = pos0;

            int lower = Math.Max(pos0, pos0 + expectedDelta - slop);
            int upper = pos0 + expectedDelta + slop;
            while (j < positions1.Count && positions1[j] < lower)
                j++;
            if (j >= positions1.Count)
                return false;
            if (positions1[j] <= upper)
                return true;
        }

        return false;
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
            return HasTwoTermPositionsWithinSlop(pos0, pos1, slop);
        }

        // 3-term specialisation: direct span access, zero allocation (matches 2-term path)
        if (termCount == 3)
        {
            var pos0 = postings[0].GetCurrentPositions();
            var pos1 = postings[1].GetCurrentPositions();
            var pos2 = postings[2].GetCurrentPositions();
            return HasThreeTermPositionsWithinSlop(pos0, pos1, pos2, slop);
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

    private static bool HasTwoTermPositionsWithinSlop(ReadOnlySpan<int> pos0, ReadOnlySpan<int> pos1, int slop)
    {
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

    private static bool HasThreeTermPositionsWithinSlop(ReadOnlySpan<int> pos0, ReadOnlySpan<int> pos1, ReadOnlySpan<int> pos2, int slop)
    {
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

    private static bool HasManyTermPositionsWithinSlop(IReadOnlyList<int[]> positions, int slop)
    {
        var cursors = new int[positions.Count];
        var firstPositions = positions[0];
        for (int i = 0; i < firstPositions.Length; i++)
        {
            int chainPos = firstPositions[i];
            bool matched = true;

            for (int t = 1; t < positions.Count; t++)
            {
                int target = chainPos + 1;
                int lo = target - slop;
                int hi = target + slop;
                var termPositions = positions[t];
                ref int cursor = ref cursors[t];
                while (cursor < termPositions.Length && termPositions[cursor] < lo)
                    cursor++;

                if (cursor >= termPositions.Length || termPositions[cursor] > hi)
                {
                    matched = false;
                    break;
                }

                chainPos = termPositions[cursor];
            }

            if (matched)
                return true;
        }

        return false;
    }

    private static bool HasMultiPhrasePositionsWithinSlop(IReadOnlyList<int[]> slotPositions, IReadOnlyList<int> expectedPositions, int slop)
    {
        if (slotPositions.Count == 0)
            return false;
        if (slotPositions.Count == 1)
            return slotPositions[0].Length > 0;

        var selected = new int[slotPositions.Count];

        return SelectPosition(0);

        bool SelectPosition(int slotIndex)
        {
            var positions = slotPositions[slotIndex];
            for (int i = 0; i < positions.Length; i++)
            {
                int candidate = positions[i];
                if (slotIndex > 0 && candidate < selected[slotIndex - 1])
                    continue;

                selected[slotIndex] = candidate;
                if (slotIndex + 1 == slotPositions.Count)
                {
                    int totalDeviation = 0;
                    for (int j = 1; j < slotPositions.Count; j++)
                    {
                        int expectedDelta = expectedPositions[j] - expectedPositions[j - 1];
                        int actualDelta = selected[j] - selected[j - 1];
                        totalDeviation += Math.Abs(actualDelta - expectedDelta);
                        if (totalDeviation > slop)
                            goto NextCandidate;
                    }

                    return true;
                }

                if (SelectPosition(slotIndex + 1))
                    return true;

            NextCandidate:
                ;
            }

            return false;
        }
    }
}

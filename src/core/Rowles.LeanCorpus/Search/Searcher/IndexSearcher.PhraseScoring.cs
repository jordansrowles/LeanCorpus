using System.Buffers;
namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Partial class containing phrase query execution and position-based scoring logic.
/// </summary>
public sealed partial class IndexSearcher
{
    private const int PhraseTwoPhaseCandidateThreshold = 1024;

    private void ExecutePhraseQuery(PhraseQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        if (query.Terms.Length == 0) return;
        if (query.Terms.Length > 1 && TryExecutePhraseQueryTwoPhase(query, reader, ref collector))
            return;

        ExecutePhraseQueryWithPositionEnums(query, reader, ref collector);
    }

    private bool TryExecutePhraseQueryTwoPhase(PhraseQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        int termCount = query.Terms.Length;
        var qualifiedTerms = query.QualifiedTerms;
        var postingsArr = new PostingsEnum[termCount];
        var candidates = ArrayPool<int>.Shared.Rent(PhraseTwoPhaseCandidateThreshold);
        int candidateCount = 0;

        try
        {
            for (int i = 0; i < termCount; i++)
            {
                postingsArr[i] = reader.GetPostingsEnum(qualifiedTerms[i]);
                if (postingsArr[i].IsExhausted)
                    return true;
            }

            int leaderIdx = 0;
            for (int i = 1; i < termCount; i++)
            {
                if (postingsArr[i].DocFreq < postingsArr[leaderIdx].DocFreq)
                    leaderIdx = i;
            }

            bool hasDeletions = reader.HasDeletions;
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

                if (!allMatch)
                    continue;

                if (candidateCount == PhraseTwoPhaseCandidateThreshold)
                    return false;

                candidates[candidateCount++] = docId;
            }

            if (candidateCount == 0)
                return true;

            int docBase = reader.DocBase;
            float score = query.Boost != 1.0f ? query.Boost : 1.0f;
            int slop = query.Slop;
            reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);
            for (int i = 0; i < candidateCount; i++)
            {
                int docId = candidates[i];
                if (HasPhrasePositionsWithinSlop(reader, qualifiedTerms, docId, slop))
                    collector.Collect(docBase + docId, ApplyFieldBoost(fieldBoosts, docId, score));
            }

            return true;
        }
        finally
        {
            for (int i = 0; i < termCount; i++)
                postingsArr[i].Dispose();
            ArrayPool<int>.Shared.Return(candidates, clearArray: false);
        }
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

    private static bool HasPhrasePositionsWithinSlop(SegmentReader reader, string[] qualifiedTerms, int docId, int slop)
    {
        if (qualifiedTerms.Length == 2)
        {
            var pos0 = reader.GetPositionsArray(qualifiedTerms[0], docId);
            var pos1 = reader.GetPositionsArray(qualifiedTerms[1], docId);
            return pos0 is { Length: > 0 } &&
                   pos1 is { Length: > 0 } &&
                   HasTwoTermPositionsWithinSlop(pos0, pos1, slop);
        }

        if (qualifiedTerms.Length == 3)
        {
            var pos0 = reader.GetPositionsArray(qualifiedTerms[0], docId);
            var pos1 = reader.GetPositionsArray(qualifiedTerms[1], docId);
            var pos2 = reader.GetPositionsArray(qualifiedTerms[2], docId);
            return pos0 is { Length: > 0 } &&
                   pos1 is { Length: > 0 } &&
                   pos2 is { Length: > 0 } &&
                   HasThreeTermPositionsWithinSlop(pos0, pos1, pos2, slop);
        }

        var positions = new int[qualifiedTerms.Length][];
        for (int i = 0; i < qualifiedTerms.Length; i++)
        {
            var termPositions = reader.GetPositionsArray(qualifiedTerms[i], docId);
            if (termPositions is not { Length: > 0 })
                return false;
            positions[i] = termPositions;
        }

        return HasManyTermPositionsWithinSlop(positions, slop);
    }

    private void ExecuteMultiPhraseQuery(MultiPhraseQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        if (query.TermGroups.Count == 0)
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

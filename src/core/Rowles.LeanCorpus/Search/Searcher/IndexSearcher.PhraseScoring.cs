namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Partial class containing phrase query execution and position-based scoring logic.
/// </summary>
public sealed partial class IndexSearcher
{
    private void ExecutePhraseQuery(PhraseQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        if (query.TermSpan.IsEmpty) return;
        ExecutePhraseQueryWithPositionEnums(query, reader, globalDFs, ref collector);
    }

    internal static float ComputePhraseFrequency(
        IReadOnlyList<int[]> positions,
        IReadOnlyList<int> queryPositions,
        int slop)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(queryPositions);

        int termCount = positions.Count;
        if (termCount == 0 || termCount != queryPositions.Count || slop < 0)
            return 0f;

        for (int i = 0; i < termCount; i++)
        {
            if (positions[i] is null || positions[i].Length == 0)
                return 0f;
        }

        if (slop == 0)
            return CountExactPhraseFrequency(positions, queryPositions);

        if (termCount == 1)
            return CountUniquePositions(positions[0]);

        var cursors = new int[termCount];
        if (!ResolvePositionCollisions(positions, queryPositions, cursors))
            return 0f;

        var offsets = new long[termCount];
        float frequency = 0f;
        while (true)
        {
            ComputeOffsetsAndEnd(positions, queryPositions, cursors, offsets, out long end);
            FindSmallestAndNextOffset(offsets, queryPositions, out int smallestSlot, out int secondSmallestSlot);
            long next = offsets[secondSmallestSlot];
            long smallestMatchLength = end - offsets[smallestSlot];

            while (true)
            {
                cursors[smallestSlot]++;
                if (cursors[smallestSlot] >= positions[smallestSlot].Length ||
                    !ResolvePositionCollisions(positions, queryPositions, cursors))
                {
                    AddSloppyFrequency(ref frequency, smallestMatchLength, slop);
                    return frequency;
                }

                ComputeOffsetsAndEnd(positions, queryPositions, cursors, offsets, out end);
                long smallestOffset = offsets[smallestSlot];
                if (smallestOffset > next)
                    break;

                long matchLength = end - smallestOffset;
                if (matchLength < smallestMatchLength)
                    smallestMatchLength = matchLength;
            }

            AddSloppyFrequency(ref frequency, smallestMatchLength, slop);
        }
    }

    private static float CountExactPhraseFrequency(
        IReadOnlyList<int[]> positions,
        IReadOnlyList<int> queryPositions)
    {
        int termCount = positions.Count;
        var selectedPositions = new int[termCount];
        var cursors = new int[termCount];
        long frequency = 0;

        int[] firstTermPositions = positions[0];
        for (int startIndex = 0; startIndex < firstTermPositions.Length; startIndex++)
        {
            int startPosition = firstTermPositions[startIndex];
            if (startIndex > 0 && startPosition == firstTermPositions[startIndex - 1])
                continue;

            selectedPositions[0] = startPosition;
            bool matches = true;
            for (int termIndex = 1; termIndex < termCount; termIndex++)
            {
                long expectedPosition = (long)startPosition + queryPositions[termIndex] - queryPositions[0];
                if (expectedPosition is < int.MinValue or > int.MaxValue)
                {
                    matches = false;
                    break;
                }

                int[] termPositions = positions[termIndex];
                int cursor = cursors[termIndex];
                while (cursor < termPositions.Length && termPositions[cursor] < expectedPosition)
                    cursor++;
                cursors[termIndex] = cursor;
                if (cursor >= termPositions.Length || termPositions[cursor] != expectedPosition)
                {
                    matches = false;
                    break;
                }

                selectedPositions[termIndex] = termPositions[cursor];
            }

            if (!matches)
                continue;

            bool usesDistinctPositions = true;
            for (int i = 0; i < selectedPositions.Length && usesDistinctPositions; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    if (selectedPositions[i] == selectedPositions[j])
                    {
                        usesDistinctPositions = false;
                        break;
                    }
                }
            }

            if (usesDistinctPositions)
                frequency++;
        }

        return frequency;
    }

    private static float CountUniquePositions(int[] positions)
    {
        int uniqueCount = 0;
        int previousPosition = 0;
        for (int i = 0; i < positions.Length; i++)
        {
            if (i == 0 || positions[i] != previousPosition)
                uniqueCount++;
            previousPosition = positions[i];
        }

        return uniqueCount;
    }

    private static bool ResolvePositionCollisions(
        IReadOnlyList<int[]> positions,
        IReadOnlyList<int> queryPositions,
        int[] cursors)
    {
        while (true)
        {
            int slotToAdvance = -1;
            for (int i = 0; i < positions.Count; i++)
            {
                for (int j = i + 1; j < positions.Count; j++)
                {
                    if (positions[i][cursors[i]] != positions[j][cursors[j]])
                        continue;

                    int candidate = queryPositions[i] > queryPositions[j]
                        ? i
                        : queryPositions[j] > queryPositions[i]
                            ? j
                            : Math.Max(i, j);
                    if (slotToAdvance < 0 ||
                        queryPositions[candidate] > queryPositions[slotToAdvance] ||
                        (queryPositions[candidate] == queryPositions[slotToAdvance] && candidate > slotToAdvance))
                    {
                        slotToAdvance = candidate;
                    }
                }
            }

            if (slotToAdvance < 0)
                return true;

            cursors[slotToAdvance]++;
            if (cursors[slotToAdvance] >= positions[slotToAdvance].Length)
                return false;
        }
    }

    private static void ComputeOffsetsAndEnd(
        IReadOnlyList<int[]> positions,
        IReadOnlyList<int> queryPositions,
        int[] cursors,
        long[] offsets,
        out long end)
    {
        end = long.MinValue;
        for (int i = 0; i < positions.Count; i++)
        {
            long offset = (long)positions[i][cursors[i]] - queryPositions[i];
            offsets[i] = offset;
            if (offset > end)
                end = offset;
        }
    }

    private static void FindSmallestAndNextOffset(
        long[] offsets,
        IReadOnlyList<int> queryPositions,
        out int smallestSlot,
        out int secondSmallestSlot)
    {
        smallestSlot = -1;
        secondSmallestSlot = -1;
        for (int i = 0; i < offsets.Length; i++)
        {
            if (smallestSlot < 0 || ComesBefore(i, smallestSlot, offsets, queryPositions))
            {
                secondSmallestSlot = smallestSlot;
                smallestSlot = i;
            }
            else if (secondSmallestSlot < 0 || ComesBefore(i, secondSmallestSlot, offsets, queryPositions))
            {
                secondSmallestSlot = i;
            }
        }
    }

    private static bool ComesBefore(
        int leftSlot,
        int rightSlot,
        long[] offsets,
        IReadOnlyList<int> queryPositions)
    {
        if (offsets[leftSlot] != offsets[rightSlot])
            return offsets[leftSlot] < offsets[rightSlot];
        if (queryPositions[leftSlot] != queryPositions[rightSlot])
            return queryPositions[leftSlot] < queryPositions[rightSlot];
        return leftSlot < rightSlot;
    }

    private static void AddSloppyFrequency(ref float frequency, long matchLength, int slop)
    {
        if (matchLength <= slop)
            frequency += 1f / ((float)matchLength + 1f);
    }

    private void ExecutePhraseQueryWithPositionEnums(PhraseQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        if (query.TermSpan.IsEmpty) return;

        int termCount = query.TermSpan.Length;
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
        int slop = query.Slop;
        reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);
        bool hasDeletions = reader.HasDeletions;
        var fieldLengths = reader.GetFieldLengthsForQuery(query.Field);
        float avgDocLength = Stats.GetAvgFieldLength(query.Field);

        // Compute scoring factors for every term, not just the leader.
        // Each term contributes its own IDF weight to the phrase score.
        var termFactors = new (float F1, float F2, float F3)[termCount];
        for (int i = 0; i < termCount; i++)
        {
            int docFreq = globalDFs.GetValueOrDefault((query.Field, query.TermSpan[i]), postingsArr[i].DocFreq);
            long collectionFreq = RequiresCollectionStatistics(query.Field)
                ? GetGlobalCollectionFreq(qualifiedTerms[i])
                : 0;
            termFactors[i] = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, query.Field);
        }
        int[] queryPositions = query.PositionSpan.ToArray();

        // Streaming merge: iterate leader, advance followers
        while (postingsArr[leaderIdx].MoveNextUnchecked(out int docId, out int leaderTf))
        {
            if (hasDeletions && !reader.IsLive(docId)) continue;

            bool allMatch = true;
            for (int i = 0; i < termCount; i++)
            {
                if (i == leaderIdx) continue;
                if (!postingsArr[i].AdvanceUnchecked(docId, out int followerDocId, out _) ||
                    followerDocId != docId)
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
                if (postingsArr[i].GetCurrentPositionsUnchecked().IsEmpty)
                {
                    hasAllPositions = false;
                    break;
                }
            }

            float phraseFrequency = 0f;
            if (hasAllPositions)
            {
                var slotPositions = new int[termCount][];
                for (int i = 0; i < termCount; i++)
                    slotPositions[i] = postingsArr[i].GetCurrentPositionsUnchecked().ToArray();

                phraseFrequency = ComputePhraseFrequency(slotPositions, queryPositions, slop);
            }

            if (phraseFrequency > 0f)
            {
                int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Value.Length
                    ? fieldLengths.Value.Span[docId] : 1;
                // Sum scores across all terms using the leader's term frequency
                // as an estimate of the phrase frequency.
                float score = 0;
                for (int i = 0; i < termCount; i++)
                {
                    var (f1, f2, f3) = termFactors[i];
                    score += ScoreTerm(f1, f2, f3, leaderTf, docLength, query.Field);
                }
                if (Math.Abs(boost - 1.0f) > 1e-6f) score *= boost;
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
        reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);

        foreach (int docId in candidateDocs!)
        {
            var slotPositions = new int[perGroupPositions.Count][];
            for (int i = 0; i < perGroupPositions.Count; i++)
                slotPositions[i] = perGroupPositions[i][docId];

            if (HasMultiPhrasePositionsWithinSlop(slotPositions, expectedPositions, query.Slop))
                collector.Collect(docBase + docId, ApplyFieldBoost(fieldBoosts, docId, baseScore));
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

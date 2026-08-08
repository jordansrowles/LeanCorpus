using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Search.Ranking;

/// <summary>Fallback behaviour when novelty cannot be calculated for a candidate pair.</summary>
public enum MissingSimilarityPolicy { TreatAsZero, ExcludeCandidate }

/// <summary>Explains one deterministic MMR selection.</summary>
public sealed record MmrSelection(ScoreDoc Document, float Relevance, float MaximumSimilarity, float SelectionValue, int Position);

/// <summary>Bounded Maximum Marginal Relevance final selection.</summary>
public static class MaximumMarginalRelevance
{
    /// <summary>Selects documents by relevance and novelty. Similarity must be finite and symmetric.</summary>
    public static IReadOnlyList<MmrSelection> Select(IReadOnlyList<ScoreDoc> candidates, int topN,
        float relevanceWeight, Func<int, int, float?> similarity, MissingSimilarityPolicy missingSimilarityPolicy = MissingSimilarityPolicy.TreatAsZero)
    {
        ArgumentNullException.ThrowIfNull(candidates); ArgumentNullException.ThrowIfNull(similarity);
        ArgumentOutOfRangeException.ThrowIfLessThan(topN, 1);
        if (!float.IsFinite(relevanceWeight) || relevanceWeight < 0 || relevanceWeight > 1) throw new ArgumentOutOfRangeException(nameof(relevanceWeight));
        var remaining = candidates.DistinctBy(static c => c.DocId).OrderByDescending(static c => c.Score).ThenBy(static c => c.DocId).ToList();
        var selected = new List<MmrSelection>(Math.Min(topN, remaining.Count));
        while (selected.Count < topN && remaining.Count > 0)
        {
            int best = -1; float bestValue = float.NegativeInfinity, bestSimilarity = 0;
            for (int i = 0; i < remaining.Count; i++)
            {
                float max = 0; bool missing = false;
                foreach (var prior in selected)
                {
                    var value = similarity(remaining[i].DocId, prior.Document.DocId);
                    if (value is null) { missing = true; continue; }
                    if (!float.IsFinite(value.Value)) { missing = true; continue; }
                    max = Math.Max(max, value.Value);
                }
                if (missing && missingSimilarityPolicy == MissingSimilarityPolicy.ExcludeCandidate) continue;
                float selection = relevanceWeight * remaining[i].Score - (1 - relevanceWeight) * max;
                if (selection > bestValue || (selection == bestValue && remaining[i].DocId < remaining[best].DocId)) { best = i; bestValue = selection; bestSimilarity = max; }
            }
            if (best < 0) break;
            var chosen = remaining[best]; remaining.RemoveAt(best);
            selected.Add(new MmrSelection(chosen, chosen.Score, bestSimilarity, bestValue, selected.Count + 1));
        }
        return selected;
    }
}

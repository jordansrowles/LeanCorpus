namespace Rowles.LeanCorpus.Search.Searcher;

public sealed partial class IndexSearcher
{
    private static float ApplyFieldBoost(SegmentReader reader, int docId, string field, float score)
    {
        float fieldBoost = reader.GetFieldBoost(docId, field);
        return fieldBoost != 1.0f ? score * fieldBoost : score;
    }

    private static float ApplyFieldBoost(float[]? fieldBoosts, int docId, float score)
    {
        if (fieldBoosts is not null && (uint)docId < (uint)fieldBoosts.Length)
        {
            float fieldBoost = fieldBoosts[docId];
            return fieldBoost != 1.0f ? score * fieldBoost : score;
        }

        return score;
    }
}

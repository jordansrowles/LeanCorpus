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

    private static float ApplyVectorQueryBoost(VectorQuery query, float score)
        => query.Boost != 1.0f ? score * query.Boost : score;

    private static float[] PrepareVectorGraphQuery(VectorQuery query, float[] queryVector, float[]? normalisedQuery)
    {
        float[] searchVector = normalisedQuery ?? queryVector;
        if (query.Boost >= 0f)
            return searchVector;

        var reversed = new float[searchVector.Length];
        for (int index = 0; index < searchVector.Length; index++)
            reversed[index] = -searchVector[index];
        return reversed;
    }

    private static void ValidateVectorQueryDimension(VectorQuery query, int fieldDimension)
    {
        if (query.QueryVector.Length != fieldDimension)
        {
            throw new ArgumentException(
                $"Query vector dimension {query.QueryVector.Length} does not match vector field '{query.Field}' dimension {fieldDimension}.",
                nameof(query));
        }
    }
}

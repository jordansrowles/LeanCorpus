using System.Numerics;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>Correction-aware 4-bit query against one-bit document scorer for v3 BBQ.</summary>
internal static class BbqOsqDistanceComputer
{
    /// <summary>Uses packed bit-plane intersections and hardware-accelerated population counts.</summary>
    internal static float ScorePacked(
        PreparedBbqQuery query,
        ReadOnlySpan<byte> packedDocument,
        BbqOsqCorrections documentCorrections,
        float centroidSquaredNorm)
    {
        ArgumentNullException.ThrowIfNull(query);
        int packedLength = checked((query.Dimension + 7) / 8);
        if (packedDocument.Length != packedLength)
            throw new ArgumentException($"Packed document length {packedDocument.Length} != required length {packedLength}.", nameof(packedDocument));

        ReadOnlySpan<byte> planes = query.BitPlanes;
        int dot = 0;
        for (int index = 0; index < packedLength; index++)
        {
            byte document = packedDocument[index];
            dot += BitOperations.PopCount((uint)(document & planes[index]));
            dot += 2 * BitOperations.PopCount((uint)(document & planes[packedLength + index]));
            dot += 4 * BitOperations.PopCount((uint)(document & planes[(packedLength * 2) + index]));
            dot += 8 * BitOperations.PopCount((uint)(document & planes[(packedLength * 3) + index]));
        }

        return CorrectedCosine(query, documentCorrections, centroidSquaredNorm, dot);
    }

    /// <summary>Dimension-by-dimension reference kernel used to verify the packed implementation.</summary>
    internal static float ScoreScalar(
        PreparedBbqQuery query,
        ReadOnlySpan<byte> packedDocument,
        BbqOsqCorrections documentCorrections,
        float centroidSquaredNorm)
    {
        ArgumentNullException.ThrowIfNull(query);
        int packedLength = checked((query.Dimension + 7) / 8);
        if (packedDocument.Length != packedLength)
            throw new ArgumentException($"Packed document length {packedDocument.Length} != required length {packedLength}.", nameof(packedDocument));

        ReadOnlySpan<byte> queryCodes = query.QueryCodes;
        int dot = 0;
        for (int index = 0; index < query.Dimension; index++)
        {
            int documentCode = (packedDocument[index >> 3] >> (7 - (index & 7))) & 1;
            dot += documentCode * queryCodes[index];
        }

        return CorrectedCosine(query, documentCorrections, centroidSquaredNorm, dot);
    }

    internal static float Score(
        PreparedBbqQuery query,
        ReadOnlySpan<byte> packedDocument,
        BbqOsqCorrections documentCorrections,
        float centroidSquaredNorm)
        => ScorePacked(query, packedDocument, documentCorrections, centroidSquaredNorm);

    private static float CorrectedCosine(
        PreparedBbqQuery query,
        BbqOsqCorrections document,
        float centroidSquaredNorm,
        int quantisedDot)
    {
        BbqOsqCorrections prepared = query.Corrections;
        if (!float.IsFinite(document.LowerInterval) || !float.IsFinite(document.UpperInterval) ||
            !float.IsFinite(document.AdditionalCorrection) || document.LowerInterval > document.UpperInterval ||
            document.CodeSum < 0 || document.CodeSum > query.Dimension ||
            !float.IsFinite(prepared.LowerInterval) || !float.IsFinite(prepared.UpperInterval) ||
            !float.IsFinite(prepared.AdditionalCorrection) || prepared.LowerInterval > prepared.UpperInterval ||
            prepared.CodeSum < 0 || prepared.CodeSum > query.Dimension * 15 ||
            !float.IsFinite(centroidSquaredNorm) || centroidSquaredNorm < 0f)
            throw new InvalidDataException("BBQ OSQ correction data is invalid.");

        // Lucene's stored document uses one bit; its prepared query uses four bits.
        // Keep the correction expression and float order equivalent to Lucene, but
        // return LeanCorpus cosine directly in [-1,1].
        float documentLower = document.LowerInterval;
        float documentStep = document.UpperInterval - documentLower;
        float queryLower = prepared.LowerInterval;
        float queryStep = (prepared.UpperInterval - queryLower) / 15f;
        float estimate = documentLower * queryLower * query.Dimension
            + queryLower * documentStep * document.CodeSum
            + documentLower * queryStep * prepared.CodeSum
            + documentStep * queryStep * quantisedDot;
        estimate += prepared.AdditionalCorrection + document.AdditionalCorrection - centroidSquaredNorm;
        if (!float.IsFinite(estimate))
            throw new InvalidDataException("BBQ OSQ produced a non-finite cosine estimate.");
        return Math.Clamp(estimate, -1f, 1f);
    }
}

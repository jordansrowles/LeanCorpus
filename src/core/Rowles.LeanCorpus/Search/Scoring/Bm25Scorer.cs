using System.Numerics;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// BM25 scorer. Computes relevance scores for term matches.
/// </summary>
public static class Bm25Scorer
{
    private const float K1 = 1.2f;
    private const float B = 0.75f;

    /// <summary>
    /// Computes the BM25 score for a single term in a single document.
    /// </summary>
    public static float Score(int termFreq, int docLength, float avgDocLength, int docCount, int docFreq)
    {
        float idf = MathF.Log(1.0f + (docCount - docFreq + 0.5f) / (docFreq + 0.5f));
        float tf = termFreq;
        float normalisedTf = (tf * (K1 + 1.0f)) / (tf + K1 * (1.0f - B + B * (docLength / avgDocLength)));
        return idf * normalisedTf;
    }

    /// <summary>Precomputes the IDF component (constant for a given term across all documents).</summary>
    public static float Idf(int docCount, int docFreq)
        => MathF.Log(1.0f + (docCount - docFreq + 0.5f) / (docFreq + 0.5f));

    /// <summary>
    /// Precomputes factors that are constant for a given field/term.
    /// Returns (idf, k1BOverAvgDL) where k1BOverAvgDL = K1 * B / avgDocLength.
    /// </summary>
    public static (float Idf, float K1BOverAvgDL) PrecomputeFactors(int docCount, int docFreq, float avgDocLength)
    {
        float idf = MathF.Log(1.0f + (docCount - docFreq + 0.5f) / (docFreq + 0.5f));
        float k1BOverAvgDL = K1 * B / avgDocLength;
        return (idf, k1BOverAvgDL);
    }

    /// <summary>
    /// Scores with precomputed IDF and normalisation factor. Avoids per-doc division.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static float ScorePrecomputed(float idf, float k1BOverAvgDL, int termFreq, int docLength)
    {
        float tf = termFreq;
        const float k1Plus1 = K1 + 1.0f;
        const float k1TimesOneMinusB = K1 * (1.0f - B);
        float normalisedTf = (tf * k1Plus1) / (tf + k1TimesOneMinusB + k1BOverAvgDL * docLength);
        return idf * normalisedTf;
    }

    /// <summary>
    /// Scores multiple documents at once using SIMD for a shared IDF term.
    /// </summary>
    /// <param name="idf">Precomputed IDF for the term.</param>
    /// <param name="k1BOverAvgDL">Precomputed K1*B/avgDocLength for the field.</param>
    /// <param name="termFreqs">Term frequencies for each document.</param>
    /// <param name="docLengths">Document lengths for each document.</param>
    /// <param name="scores">Output scores. Must be at least as long as termFreqs and docLengths.</param>
    public static void ScorePrecomputedBatch(
        float idf, float k1BOverAvgDL,
        ReadOnlySpan<int> termFreqs, ReadOnlySpan<int> docLengths,
        Span<float> scores)
    {
        int length = termFreqs.Length;
        const float k1Plus1 = K1 + 1.0f;
        const float k1TimesOneMinusB = K1 * (1.0f - B);

        var vIdf = new Vector<float>(idf);
        var vK1Plus1 = new Vector<float>(k1Plus1);
        var vK1TimesOneMinusB = new Vector<float>(k1TimesOneMinusB);
        var vK1BOverAvgDL = new Vector<float>(k1BOverAvgDL);

        int i = 0;

#if NET11_0_OR_GREATER
        if (length >= Vector<float>.Count)
#else
        if (Vector.IsHardwareAccelerated && length >= Vector<float>.Count)
#endif
        {
            int vecCount = Vector<float>.Count;
            int simdEnd = length - (length % vecCount);

            Span<float> tfBuf = stackalloc float[vecCount];
            Span<float> dlBuf = stackalloc float[vecCount];

            for (; i < simdEnd; i += vecCount)
            {
                for (int j = 0; j < vecCount; j++)
                {
                    tfBuf[j] = termFreqs[i + j];
                    dlBuf[j] = docLengths[i + j];
                }

                var vTf = new Vector<float>(tfBuf);
                var vDl = new Vector<float>(dlBuf);

                var numerator = vTf * vK1Plus1;
                var denominator = vTf + vK1TimesOneMinusB + vK1BOverAvgDL * vDl;
                var result = vIdf * numerator / denominator;

                result.CopyTo(scores.Slice(i));
            }
        }

        for (; i < length; i++)
        {
            float tf = termFreqs[i];
            float dl = docLengths[i];
            scores[i] = idf * (tf * k1Plus1) / (tf + k1TimesOneMinusB + k1BOverAvgDL * dl);
        }
    }

    /// <summary>
    /// Normalises a field term frequency for BM25F-style combined-field scoring before cross-field aggregation.
    /// </summary>
    public static float NormaliseFieldTermFrequency(float termFreq, int docLength, float avgDocLength, float fieldWeight = 1.0f)
    {
        float denominator = 1.0f - B + B * (docLength / avgDocLength);
        return denominator <= 0f ? 0f : (fieldWeight * termFreq) / denominator;
    }

    /// <summary>
    /// Scores a BM25F-style pseudo term frequency using a precomputed IDF.
    /// </summary>
    public static float ScoreCombinedWithIdf(float idf, float pseudoTermFrequency)
    {
        if (pseudoTermFrequency <= 0f)
            return 0f;

        return idf * ((pseudoTermFrequency * (K1 + 1.0f)) / (pseudoTermFrequency + K1));
    }
}

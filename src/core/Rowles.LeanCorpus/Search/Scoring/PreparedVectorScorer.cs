using System.Numerics;
using System.Runtime.InteropServices;
using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Reusable query-side state for scoring many vectors with one similarity function.
/// </summary>
/// <remarks>
/// The query is copied at construction so callers can safely reuse their input buffer.
/// This keeps similarity dispatch and query norm preparation outside document-scoring loops.
/// </remarks>
public sealed class PreparedVectorScorer
{
    private readonly float[] _query;
    private readonly VectorSimilarityFunction _similarity;
    private readonly float _querySquaredNorm;

    /// <summary>Initialises scorer state for a non-empty finite query vector.</summary>
    public PreparedVectorScorer(
        ReadOnlySpan<float> query,
        VectorSimilarityFunction similarity = VectorSimilarityFunction.Cosine)
    {
        if (query.IsEmpty)
            throw new ArgumentException("Query vectors must contain at least one dimension.", nameof(query));
        if (!Enum.IsDefined(similarity))
            throw new ArgumentOutOfRangeException(nameof(similarity));

        _query = query.ToArray();
        float squaredNorm = 0f;
        for (int i = 0; i < _query.Length; i++)
        {
            if (!float.IsFinite(_query[i]))
                throw new ArgumentException("Query vectors must contain only finite values.", nameof(query));
            squaredNorm += _query[i] * _query[i];
        }
        _similarity = similarity;
        _querySquaredNorm = squaredNorm;
    }

    /// <summary>Prepared vector dimension.</summary>
    public int Dimension => _query.Length;

    /// <summary>Configured similarity function.</summary>
    public VectorSimilarityFunction Similarity => _similarity;

    /// <summary>Scores one vector. A mismatched or empty vector scores zero.</summary>
    public float Score(ReadOnlySpan<float> vector)
    {
        if (vector.Length != _query.Length || vector.IsEmpty)
            return 0f;

        float dot = 0f;
        float vectorSquaredNorm = 0f;
        float squaredDistance = 0f;
        int hammingDistance = 0;
        int i = 0;

        if (vector.Length >= Vector<float>.Count)
        {
            int simdEnd = vector.Length - vector.Length % Vector<float>.Count;
            var queryVectors = MemoryMarshal.Cast<float, Vector<float>>(_query.AsSpan(0, simdEnd));
            var candidateVectors = MemoryMarshal.Cast<float, Vector<float>>(vector[..simdEnd]);
            var dotVector = Vector<float>.Zero;
            var normVector = Vector<float>.Zero;
            var distanceVector = Vector<float>.Zero;
            for (int block = 0; block < queryVectors.Length; block++)
            {
                Vector<float> candidate = candidateVectors[block];
                Vector<float> delta = queryVectors[block] - candidate;
                dotVector += queryVectors[block] * candidate;
                normVector += candidate * candidate;
                distanceVector += delta * delta;
            }
            dot = Vector.Sum(dotVector);
            vectorSquaredNorm = Vector.Sum(normVector);
            squaredDistance = Vector.Sum(distanceVector);
            i = simdEnd;
        }

        for (; i < vector.Length; i++)
        {
            float candidate = vector[i];
            dot += _query[i] * candidate;
            vectorSquaredNorm += candidate * candidate;
            float delta = _query[i] - candidate;
            squaredDistance += delta * delta;
            if (_query[i] != candidate)
                hammingDistance++;
        }

        // Hamming equality cannot use a floating SIMD comparison without changing its
        // exact bitwise contract, so account for the SIMD prefix here.
        if (_similarity == VectorSimilarityFunction.Hamming && i > 0)
        {
            int simdEnd = vector.Length - vector.Length % Vector<float>.Count;
            for (int index = 0; index < simdEnd; index++)
                if (_query[index] != vector[index])
                    hammingDistance++;
        }

        return _similarity switch
        {
            VectorSimilarityFunction.Cosine =>
                _querySquaredNorm > 0f && vectorSquaredNorm > 0f
                    ? dot / MathF.Sqrt(_querySquaredNorm * vectorSquaredNorm)
                    : 0f,
            VectorSimilarityFunction.DotProduct => dot,
            VectorSimilarityFunction.Euclidean => 1f / (1f + squaredDistance),
            VectorSimilarityFunction.MaximumInnerProduct => dot < 0f ? 1f / (1f - dot) : dot + 1f,
            VectorSimilarityFunction.Hamming => 1f - (float)hammingDistance / vector.Length,
            _ => throw new ArgumentOutOfRangeException(nameof(_similarity)),
        };
    }
}

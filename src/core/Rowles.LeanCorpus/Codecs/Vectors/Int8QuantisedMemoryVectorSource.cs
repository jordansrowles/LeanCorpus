using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// In-memory <see cref="IVectorSource"/> that holds vectors quantised to int8 in a single
/// contiguous byte buffer. Used during HNSW graph construction to reduce peak memory
/// from ~3 GB (float32) to ~768 MB for 1M × 768-dim vectors.
/// </summary>
/// <remarks>
/// <see cref="GetVector"/> allocates a fresh <c>float[Dimension]</c> and dequantises on demand,
/// matching the allocation pattern of <see cref="VectorReader"/>. This is called once per
/// vector per HNSW layer during graph construction — acceptable overhead for the memory savings.
/// </remarks>
internal sealed class Int8QuantisedMemoryVectorSource : IVectorSource, IInt8VectorSource
{
    private readonly byte[] _packed;
    private readonly float _min;
    private readonly float _alpha;
    private readonly int _docCount;
    private readonly int _dimension;

    /// <summary>
    /// Quantises all vectors from the source dictionary into an internal byte buffer.
    /// After construction, the caller may release the source dictionary to reclaim memory.
    /// </summary>
    /// <param name="vectorsByDoc">Source vectors. Only entries whose key is in [0, docCount) are stored.</param>
    /// <param name="dimension">Fixed dimension of every vector.</param>
    /// <param name="min">Per-segment minimum value across all dimensions.</param>
    /// <param name="alpha">Per-segment scale factor: <c>(max - min) / 255</c>.</param>
    public Int8QuantisedMemoryVectorSource(
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> vectorsByDoc,
        int dimension,
        float min,
        float alpha)
    {
        ArgumentNullException.ThrowIfNull(vectorsByDoc);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);

        _docCount = vectorsByDoc.Count;
        _dimension = dimension;
        _min = min;
        _alpha = alpha;

        _packed = new byte[_docCount * dimension];

        Span<float> zero = dimension <= 256 ? stackalloc float[dimension] : new float[dimension];
        zero.Clear();

        for (int i = 0; i < _docCount; i++)
        {
            ReadOnlySpan<float> span = zero;
            if (vectorsByDoc.TryGetValue(i, out var v))
                span = v.Span;

            int offset = i * dimension;
            for (int j = 0; j < dimension; j++)
            {
                float orig = span[j];
                float clamped = Math.Clamp((orig - _min) / _alpha + 0.5f, 0f, 255f);
                _packed[offset + j] = (byte)clamped;
            }
        }
    }

    public int Dimension => _dimension;
    public int Count => _docCount;

    /// <summary>
    /// Dequantises the stored int8 vector into a freshly allocated float array.
    /// The caller owns the returned array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<float> GetVector(int docId)
    {
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));

        var vec = new float[_dimension];
        int offset = docId * _dimension;
        for (int j = 0; j < _dimension; j++)
            vec[j] = _min + _alpha * _packed[offset + j];

        return vec;
    }

    /// <inheritdoc cref="IInt8VectorSource.GetRawVector"/>
    ReadOnlySpan<byte> IInt8VectorSource.GetRawVector(int docId)
    {
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));
        return new ReadOnlySpan<byte>(_packed, docId * _dimension, _dimension);
    }

    /// <inheritdoc cref="IInt8VectorSource.Min"/>
    float IInt8VectorSource.Min => _min;

    /// <inheritdoc cref="IInt8VectorSource.Alpha"/>
    float IInt8VectorSource.Alpha => _alpha;
}

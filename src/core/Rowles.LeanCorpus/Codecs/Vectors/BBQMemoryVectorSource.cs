using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// In-memory <see cref="IVectorSource"/> holding BBQ-quantised vectors as bit-packed bytes.
/// Used during HNSW graph construction so the graph structure is optimised for the same
/// PopCount-based distance metric used at search time.
/// </summary>
/// <remarks>
/// <see cref="GetVector"/> dequantises on demand (<c>centroid[j] ± 1</c>), allocating a fresh float array.
/// <see cref="GetRawVector"/> returns the packed bits for fast PopCount distance during graph traversal.
/// </remarks>
internal sealed class BBQMemoryVectorSource : IBBQVectorSource
{
    private readonly byte[] _packed;
    private readonly float[] _centroid;
    private readonly int _docCount;
    private readonly int _dimension;
    private readonly int _packedBytes;

    /// <summary>
    /// Binary-quantises all vectors from the source dictionary into an internal bit-packed buffer.
    /// After construction, the caller may release the source dictionary to reclaim memory.
    /// </summary>
    /// <param name="vectorsByDoc">Source vectors indexed by document ID.</param>
    /// <param name="dimension">Fixed dimension of every vector.</param>
    /// <param name="centroid">Per-segment centroid for mean removal before binary quantisation.</param>
    public BBQMemoryVectorSource(
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> vectorsByDoc,
        int dimension,
        float[] centroid)
    {
        ArgumentNullException.ThrowIfNull(vectorsByDoc);
        ArgumentNullException.ThrowIfNull(centroid);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        if (centroid.Length != dimension)
            throw new ArgumentException($"Centroid dimension {centroid.Length} != {dimension}.", nameof(centroid));

        _docCount = vectorsByDoc.Count;
        _dimension = dimension;
        _centroid = centroid;
        _packedBytes = (dimension + 7) / 8;
        _packed = new byte[_docCount * _packedBytes];

        Span<float> zero = dimension <= 256 ? stackalloc float[dimension] : new float[dimension];
        zero.Clear();

        for (int i = 0; i < _docCount; i++)
        {
            ReadOnlySpan<float> span = zero;
            if (vectorsByDoc.TryGetValue(i, out var v))
                span = v.Span;

            int offset = i * _packedBytes;
            for (int j = 0; j < dimension; j++)
            {
                float residual = span[j] - _centroid[j];
                if (residual > 0f)
                {
                    int byteIdx = offset + (j / 8);
                    int bitIdx = j % 8;
                    _packed[byteIdx] |= (byte)(1 << bitIdx);
                }
            }
        }
    }

    public int Dimension => _dimension;
    public int Count => _docCount;
    public ReadOnlySpan<float> Centroid => _centroid;

    /// <summary>Dequantises into a freshly allocated float array: centroid[j] ± 1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<float> GetVector(int docId)
    {
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));

        var vec = new float[_dimension];
        int offset = docId * _packedBytes;
        for (int j = 0; j < _dimension; j++)
        {
            int byteIdx = offset + (j / 8);
            int bitIdx = j % 8;
            float sign = ((_packed[byteIdx] >> bitIdx) & 1) == 1 ? 1f : -1f;
            vec[j] = _centroid[j] + sign;
        }
        return vec;
    }

    /// <summary>Returns the bit-packed bytes for PopCount distance computation. Length = ceil(Dimension / 8).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> GetRawVector(int docId)
    {
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));

        int offset = docId * _packedBytes;
        return _packed.AsSpan(offset, _packedBytes);
    }
}

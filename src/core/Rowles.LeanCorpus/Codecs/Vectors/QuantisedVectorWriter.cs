using System.Buffers;
using System.Runtime.InteropServices;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Writes quantised dense vectors in the <c>.vq</c> format. Supports int8 scalar quantisation
/// and Better Binary Quantisation (BBQ). The file format is self-describing: the quantisation
/// type is encoded in the header so the reader can validate against segment metadata.
/// </summary>
/// <remarks>
/// File format:
/// <code>
/// [magic:int32][version:byte=1]
/// [docCount:int32][dimension:int32]
/// [quantisation:byte]
/// -- int8 (quantisation=1) --
/// [min:float32][alpha:float32]
/// per doc: [correction:float32]
/// packed: [docCount * dimension:byte]
/// -- BBQ (quantisation=2) --
/// [centroid:float32[dimension]]
/// per doc: [correction:float32 * 3]
/// packed: [docCount * ceil(dimension/8):byte]
/// </code>
/// </remarks>
internal static class QuantisedVectorWriter
{
    private const float Epsilon = 1e-8f;

    /// <summary>
    /// Writes int8 scalar-quantised vectors. Uses per-segment min/max to compute
    /// a uniform scale factor (alpha), then quantises each float to [0, 255].
    /// A per-vector correction float is stored for future exact reranking.
    /// </summary>
    internal static void WriteInt8(
        string filePath,
        int docCount,
        int dimension,
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> vectorsByDoc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(docCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);

        // --- Pass 1: compute per-segment min / max ---
        float min = float.MaxValue;
        float max = float.MinValue;
        bool any = false;
        foreach (var v in vectorsByDoc.Values)
        {
            var span = v.Span;
            for (int j = 0; j < span.Length; j++)
            {
                float val = span[j];
                if (val < min) min = val;
                if (val > max) max = val;
                any = true;
            }
        }

        if (!any)
        {
            min = 0f;
            max = 1f;
        }
        else if (Math.Abs(max - min) < Epsilon)
        {
            max = min + 1f; // avoid zero alpha
        }

        float alpha = (max - min) / 255f;

        var bodyBuf = new ArrayBufferWriter<byte>(4096);

        bodyBuf.WriteInt32(docCount);
        bodyBuf.WriteInt32(dimension);
        bodyBuf.WriteByte((byte)VectorQuantisation.Int8);
        bodyBuf.WriteSingle(min);
        bodyBuf.WriteSingle(alpha);

        // --- Pass 2: quantise, write corrections, save packed bytes ---
        Span<float> zero = dimension <= 256 ? stackalloc float[dimension] : new float[dimension];
        zero.Clear();

        // Store each doc's packed bytes so Pass 3 writes them without re-quantising.
        var packedDocs = new List<byte[]>(docCount);
        byte[] packedBuffer = ArrayPool<byte>.Shared.Rent(dimension);
        try
        {
            for (int docId = 0; docId < docCount; docId++)
            {
                ReadOnlySpan<float> span = zero;
                if (vectorsByDoc.TryGetValue(docId, out var v))
                    span = v.Span;

                float correction = 0f;
                for (int j = 0; j < dimension; j++)
                {
                    float orig = span[j];
                    float clamped = Math.Clamp((orig - min) / alpha + 0.5f, 0f, 255f);
                    byte qv = (byte)clamped;
                    packedBuffer[j] = qv;

                    float reconstructed = min + alpha * qv;
                    float error = orig - reconstructed;
                    correction += alpha * qv * error;
                }

                bodyBuf.WriteSingle(correction);

                // Save packed bytes for Pass 3.
                var packed = new byte[dimension];
                Array.Copy(packedBuffer, 0, packed, 0, dimension);
                packedDocs.Add(packed);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packedBuffer, clearArray: false);
        }

        // --- Pass 3: write packed bytes (no re-quantisation) ---
        foreach (var packed in packedDocs)
            bodyBuf.WriteBytes(packed, 0, packed.Length);

        using var output = new IndexOutput(filePath);
        CodecFileHeader.Write(output, CodecFormats.QuantisedVectors, bodyBuf.WrittenSpan);
    }

    /// <summary>
    /// Writes BBQ (Better Binary Quantisation) vectors. Uses a per-segment centroid
    /// for mean removal, then binary quantises each dimension. Query-side int4
    /// quantisation enables efficient PopCount-based asymmetric distance.
    /// Three per-vector correction floats are stored for dot-product recovery.
    /// </summary>
    internal static void WriteBBQ(
        string filePath,
        int docCount,
        int dimension,
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> vectorsByDoc,
        ReadOnlySpan<float> centroid)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(docCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        if (centroid.Length != dimension)
            throw new ArgumentException($"Centroid dimension {centroid.Length} != {dimension}.", nameof(centroid));

        int packedBytes = (dimension + 7) / 8;

        var bodyBuf = new ArrayBufferWriter<byte>(4096);

        bodyBuf.WriteInt32(docCount);
        bodyBuf.WriteInt32(dimension);
        bodyBuf.WriteByte((byte)VectorQuantisation.BBQ);

        // Write centroid
        for (int j = 0; j < dimension; j++)
            bodyBuf.WriteSingle(centroid[j]);

        Span<float> zero = dimension <= 256 ? stackalloc float[dimension] : new float[dimension];
        zero.Clear();

        // Store each doc's packed bits so Pass 2 writes them without re-quantising.
        var packedDocs = new List<byte[]>(docCount);
        byte[] bitBuf = ArrayPool<byte>.Shared.Rent(packedBytes);
        try
        {
            for (int docId = 0; docId < docCount; docId++)
            {
                ReadOnlySpan<float> span = zero;
                if (vectorsByDoc.TryGetValue(docId, out var v))
                    span = v.Span;

                bitBuf.AsSpan(0, packedBytes).Clear();

                // Compute error corrections and binary quantise
                float corr1 = 0f;
                float corr2 = 0f;
                float corr3 = 0f;
                for (int j = 0; j < dimension; j++)
                {
                    float residual = span[j] - centroid[j];
                    if (residual > 0f)
                    {
                        int byteIdx = j / 8;
                        int bitIdx = j % 8;
                        bitBuf[byteIdx] |= (byte)(1 << bitIdx);
                    }

                    float sign = residual > 0f ? 1f : -1f;
                    corr1 += residual;
                    corr2 += sign * residual;
                    corr3 += residual * residual;
                }

                bodyBuf.WriteSingle(corr1);
                bodyBuf.WriteSingle(corr2);
                bodyBuf.WriteSingle(corr3);

                // Save packed bits for Pass 2.
                var packed = new byte[packedBytes];
                Array.Copy(bitBuf, 0, packed, 0, packedBytes);
                packedDocs.Add(packed);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bitBuf, clearArray: false);
        }

        // --- Pass 2: write bit-packed data (no re-quantisation) ---
        foreach (var packed in packedDocs)
            bodyBuf.WriteBytes(packed, 0, packed.Length);

        using var output = new IndexOutput(filePath);
        CodecFileHeader.Write(output, CodecFormats.QuantisedVectors, bodyBuf.WrittenSpan);
    }
}

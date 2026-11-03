using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Writes supported Int8 dense vectors in the <c>.vq</c> format. BBQ entry points reject retired writes.
/// The file format is self-describing: the quantisation
/// type is encoded in the header so the reader can validate against segment metadata.
/// </summary>
/// <remarks>
/// File format:
/// <code>
/// Canonical CodecKit frame, body version 3:
/// [docCount:int32][dimension:int32]
/// [quantisation:byte][presenceBitmap:ceil(docCount/8) bytes][storageKind:byte]
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
    /// Rejects creation of the retired experimental BBQ representation before touching files.
    /// </summary>
    internal static void WriteBBQOsqCanonical(
        string filePath,
        int docCount,
        int dimension,
        IVectorSource vectorsByDoc,
        IReadOnlyList<int> vectorDocIds)
        => throw VectorCodecFiles.RetiredBbqWrite();

    /// <summary>Writes Int8 vectors through bounded passes over a file-backed source.</summary>
    internal static void WriteInt8(
        string filePath,
        int docCount,
        int dimension,
        IVectorSource vectorsByDoc,
        IReadOnlyList<int> vectorDocIds)
    {
        ValidateSource(docCount, dimension, vectorsByDoc, vectorDocIds);
        var presence = VectorPresence.Create(docCount, vectorDocIds);
        float[] vectorBuffer = ArrayPool<float>.Shared.Rent(dimension);
        byte[] packedBuffer = ArrayPool<byte>.Shared.Rent(dimension);
        try
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            bool any = false;
            Span<float> calibrationVector = vectorBuffer.AsSpan(0, dimension);
            foreach (int docId in vectorDocIds)
            {
                vectorsByDoc.CopyVectorTo(docId, calibrationVector);
                for (int j = 0; j < dimension; j++)
                {
                    float value = calibrationVector[j];
                    if (value < min) min = value;
                    if (value > max) max = value;
                    any = true;
                }
            }

            if (!any)
            {
                min = 0f;
                max = 1f;
            }
            else if (MathF.Abs(max - min) < Epsilon)
            {
                max = min + 1f;
            }
            float alpha = (max - min) / 255f;

            CodecFileWriter.WriteAtomically(filePath, VectorCodecFiles.Quantised,
            VectorCodecFiles.QuantisedIdentity(QuantisedVectorStorageKind.LegacyInt8), durable: false, bodyOutput =>
            {
                Span<float> vector = vectorBuffer.AsSpan(0, dimension);
                bodyOutput.WriteInt32(docCount);
                bodyOutput.WriteInt32(dimension);
                bodyOutput.WriteByte((byte)VectorQuantisation.Int8);
                bodyOutput.WriteBytes(presence, 0, presence.Length);
                bodyOutput.WriteByte((byte)QuantisedVectorStorageKind.LegacyInt8);
                bodyOutput.WriteSingle(min);
                bodyOutput.WriteSingle(alpha);

                // Corrections precede packed data in the persisted format, so reread
                // the mapped source instead of retaining one packed array per document.
                for (int docId = 0; docId < docCount; docId++)
                {
                    vectorsByDoc.CopyVectorTo(docId, vector);
                    float correction = 0f;
                    for (int j = 0; j < dimension; j++)
                    {
                        float quantised = Math.Clamp((vector[j] - min) / alpha + 0.5f, 0f, 255f);
                        float reconstructed = min + alpha * quantised;
                        correction += alpha * quantised * (vector[j] - reconstructed);
                    }
                    bodyOutput.WriteSingle(correction);
                }

                for (int docId = 0; docId < docCount; docId++)
                {
                    vectorsByDoc.CopyVectorTo(docId, vector);
                    for (int j = 0; j < dimension; j++)
                        packedBuffer[j] = (byte)Math.Clamp((vector[j] - min) / alpha + 0.5f, 0f, 255f);
                    bodyOutput.WriteBytes(packedBuffer, 0, dimension);
                }
            });
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packedBuffer, clearArray: false);
            ArrayPool<float>.Shared.Return(vectorBuffer, clearArray: false);
        }
    }

    /// <summary>Writes BBQ vectors through bounded passes over a file-backed source.</summary>
    internal static void WriteBBQ(
        string filePath,
        int docCount,
        int dimension,
        IVectorSource vectorsByDoc,
        IReadOnlyList<int> vectorDocIds)
        => throw VectorCodecFiles.RetiredBbqWrite();

    private static void ValidateSource(
        int docCount,
        int dimension,
        IVectorSource vectorsByDoc,
        IReadOnlyList<int> vectorDocIds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(docCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        ArgumentNullException.ThrowIfNull(vectorsByDoc);
        ArgumentNullException.ThrowIfNull(vectorDocIds);
        if (vectorsByDoc.Dimension != dimension || vectorsByDoc.Count < docCount)
            throw new ArgumentException("Vector source dimensions do not match the destination field.", nameof(vectorsByDoc));
        foreach (int docId in vectorDocIds)
        {
            if ((uint)docId >= (uint)docCount)
                throw new ArgumentOutOfRangeException(nameof(vectorDocIds), $"Vector document ID {docId} is outside the destination document range.");
        }
    }

    private static void ValidateSparseSource(
        int docCount,
        int dimension,
        IVectorSource vectorsByDoc,
        IReadOnlyList<int> vectorDocIds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(docCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        ArgumentNullException.ThrowIfNull(vectorsByDoc);
        ArgumentNullException.ThrowIfNull(vectorDocIds);
        if (vectorsByDoc.Dimension != dimension)
            throw new ArgumentException("Vector source dimensions do not match the destination field.", nameof(vectorsByDoc));

        foreach (int docId in vectorDocIds)
        {
            if ((uint)docId >= (uint)docCount)
                throw new ArgumentOutOfRangeException(nameof(vectorDocIds),
                    $"Vector document ID {docId} is outside the destination document range.");
            if (docId >= vectorsByDoc.Count)
                throw new ArgumentOutOfRangeException(nameof(vectorDocIds),
                    $"Vector source does not address present document {docId}.");
        }
    }

    /// <summary>
    /// Writes int8 scalar-quantised vectors. Uses per-segment min/max to compute
    /// a uniform scale factor (alpha), then quantises each float to [0, 255].
    /// A per-vector correction float is stored for rescoring the quantised values.
    /// </summary>
    internal static void WriteInt8(
        string filePath,
        int docCount,
        int dimension,
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> vectorsByDoc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(docCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);

        VectorPresence.ValidateVectors(docCount, dimension, vectorsByDoc);
        var presence = VectorPresence.Create(docCount, vectorsByDoc.Keys);

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

        CodecFileWriter.WriteAtomically(filePath, VectorCodecFiles.Quantised,
            VectorCodecFiles.QuantisedIdentity(QuantisedVectorStorageKind.LegacyInt8), durable: false, bodyOutput =>
        {
        bodyOutput.WriteInt32(docCount);
        bodyOutput.WriteInt32(dimension);
        bodyOutput.WriteByte((byte)VectorQuantisation.Int8);
        bodyOutput.WriteBytes(presence, 0, presence.Length);
        bodyOutput.WriteByte((byte)QuantisedVectorStorageKind.LegacyInt8);
        bodyOutput.WriteSingle(min);
        bodyOutput.WriteSingle(alpha);

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

                bodyOutput.WriteSingle(correction);

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
            bodyOutput.WriteBytes(packed, 0, packed.Length);
        });
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
        => throw VectorCodecFiles.RetiredBbqWrite();
}

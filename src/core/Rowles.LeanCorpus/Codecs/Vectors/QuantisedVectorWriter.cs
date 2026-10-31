using System.Buffers;
using System.Runtime.InteropServices;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Writes quantised dense vectors in the <c>.vq</c> format. Supports int8 scalar quantisation
/// and Better Binary Quantisation (BBQ). The file format is self-describing: the quantisation
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
    /// Writes the current BBQ representation. Canonical records are created once from
    /// the supplied vectors, then decoded and temporarily normalised for OSQ calibration.
    /// </summary>
    internal static void WriteBBQOsqCanonical(
        string filePath,
        int docCount,
        int dimension,
        IVectorSource vectorsByDoc,
        IReadOnlyList<int> vectorDocIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ValidateSparseSource(docCount, dimension, vectorsByDoc, vectorDocIds);
        if (dimension < 128)
            throw new ArgumentOutOfRangeException(nameof(dimension), dimension,
                "New BBQ v3 fields require at least 128 dimensions.");

        byte[] presence = VectorPresence.Create(docCount, vectorDocIds);
        int packedBytes = checked((dimension + 7) / 8);
        string canonicalPath = string.Concat(filePath, ".", Guid.NewGuid().ToString("N"), ".canonical.tmp");
        string osqCodesPath = string.Concat(filePath, ".", Guid.NewGuid().ToString("N"), ".osq.tmp");

        try
        {
            CanonicalInt8VectorWriter.WriteRecords(canonicalPath, docCount, dimension, vectorsByDoc, vectorDocIds);
            using CanonicalInt8VectorReader canonical = CanonicalInt8VectorReader.OpenRecords(canonicalPath, docCount, dimension, presence);

            float[] vectorBuffer = ArrayPool<float>.Shared.Rent(dimension);
            float[] residualBuffer = ArrayPool<float>.Shared.Rent(dimension);
            byte[] codeBuffer = ArrayPool<byte>.Shared.Rent(dimension);
            byte[] packedBuffer = ArrayPool<byte>.Shared.Rent(packedBytes);
            float[] centroidBuffer = ArrayPool<float>.Shared.Rent(dimension);
            try
            {
                Span<float> vector = vectorBuffer.AsSpan(0, dimension);
                Span<float> centroid = centroidBuffer.AsSpan(0, dimension);
                centroid.Clear();

                foreach (int docId in vectorDocIds)
                {
                    canonical.CopyVectorTo(docId, vector);
                    _ = VectorMath.NormaliseInPlace(vector);
                    for (int index = 0; index < dimension; index++)
                        centroid[index] += vector[index];
                }

                if (vectorDocIds.Count > 0)
                {
                    float divisor = vectorDocIds.Count;
                    for (int index = 0; index < dimension; index++)
                        centroid[index] /= divisor;
                    _ = VectorMath.NormaliseInPlace(centroid);
                }

                float centroidSquaredNorm = Dot(centroid, centroid);
                CodecFileWriter.WriteAtomically(filePath, VectorCodecFiles.Quantised, durable: false, bodyOutput =>
                {
                    Span<float> outputVector = vectorBuffer.AsSpan(0, dimension);
                    Span<float> outputResiduals = residualBuffer.AsSpan(0, dimension);
                    Span<byte> outputCodes = codeBuffer.AsSpan(0, dimension);
                    Span<byte> outputPacked = packedBuffer.AsSpan(0, packedBytes);
                    Span<float> outputCentroid = centroidBuffer.AsSpan(0, dimension);

                    bodyOutput.WriteInt32(docCount);
                    bodyOutput.WriteInt32(dimension);
                    bodyOutput.WriteByte((byte)VectorQuantisation.BBQ);
                    bodyOutput.WriteBytes(presence, 0, presence.Length);
                    bodyOutput.WriteByte((byte)QuantisedVectorStorageKind.BbqOsqCanonicalInt8);
                    bodyOutput.WriteByte((byte)CanonicalVectorEncoding.PerVectorUnsigned8);
                    for (int index = 0; index < dimension; index++)
                        bodyOutput.WriteSingle(outputCentroid[index]);
                    bodyOutput.WriteSingle(centroidSquaredNorm);

                    using (var codeOutput = new IndexOutput(osqCodesPath,
                               preallocationSize: checked((long)docCount * packedBytes)))
                    {
                        for (int docId = 0; docId < docCount; docId++)
                        {
                            if (!canonical.HasVector(docId))
                            {
                                bodyOutput.WriteSingle(0f);
                                bodyOutput.WriteSingle(0f);
                                bodyOutput.WriteSingle(0f);
                                bodyOutput.WriteInt32(0);
                                outputPacked.Clear();
                                codeOutput.WriteBytes(outputPacked);
                                continue;
                            }

                            canonical.CopyVectorTo(docId, outputVector);
                            _ = VectorMath.NormaliseInPlace(outputVector);
                            BbqOsqCorrections corrections = OptimizedScalarQuantiser.Quantize(
                                outputVector, outputCentroid, bits: 1, outputResiduals, outputCodes);
                            OptimizedScalarQuantiser.PackBinary(outputCodes, outputPacked);
                            bodyOutput.WriteSingle(corrections.LowerInterval);
                            bodyOutput.WriteSingle(corrections.UpperInterval);
                            bodyOutput.WriteSingle(corrections.AdditionalCorrection);
                            bodyOutput.WriteInt32(corrections.CodeSum);
                            codeOutput.WriteBytes(outputPacked);
                        }
                    }

                    using (var codeInput = new IndexInput(osqCodesPath))
                    {
                        for (int docId = 0; docId < docCount; docId++)
                        {
                            long position = (long)docId * packedBytes;
                            bodyOutput.WriteBytes(codeInput.BorrowSpan(packedBytes, ref position));
                        }
                    }

                    for (int docId = 0; docId < docCount; docId++)
                        canonical.CopyCanonicalMetadataTo(docId, bodyOutput);
                    for (int docId = 0; docId < docCount; docId++)
                        canonical.CopyCanonicalCodesTo(docId, bodyOutput);
                });
            }
            finally
            {
                ArrayPool<float>.Shared.Return(centroidBuffer, clearArray: true);
                ArrayPool<byte>.Shared.Return(packedBuffer, clearArray: true);
                ArrayPool<byte>.Shared.Return(codeBuffer, clearArray: true);
                ArrayPool<float>.Shared.Return(residualBuffer, clearArray: true);
                ArrayPool<float>.Shared.Return(vectorBuffer, clearArray: true);
            }
        }
        finally
        {
            TryDeleteTemporary(canonicalPath);
            TryDeleteTemporary(osqCodesPath);
        }
    }

    private static float Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        float dot = 0f;
        for (int index = 0; index < left.Length; index++)
            dot += left[index] * right[index];
        return dot;
    }

    private static void TryDeleteTemporary(string path)
        => FileOpenRetry.Delete(path);

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

            CodecFileWriter.WriteAtomically(filePath, VectorCodecFiles.Quantised, durable: false, bodyOutput =>
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
    {
        ValidateSource(docCount, dimension, vectorsByDoc, vectorDocIds);
        var presence = VectorPresence.Create(docCount, vectorDocIds);
        int packedBytes = checked((dimension + 7) / 8);
        float[] vectorBuffer = ArrayPool<float>.Shared.Rent(dimension);
        float[] centroidBuffer = ArrayPool<float>.Shared.Rent(dimension);
        byte[] packedBuffer = ArrayPool<byte>.Shared.Rent(packedBytes);
        try
        {
            Span<float> calibrationVector = vectorBuffer.AsSpan(0, dimension);
            Span<float> centroid = centroidBuffer.AsSpan(0, dimension);
            centroid.Clear();
            foreach (int docId in vectorDocIds)
            {
                vectorsByDoc.CopyVectorTo(docId, calibrationVector);
                for (int j = 0; j < dimension; j++)
                    centroid[j] += calibrationVector[j];
            }
            if (vectorDocIds.Count > 0)
            {
                float divisor = vectorDocIds.Count;
                for (int j = 0; j < dimension; j++)
                    centroid[j] /= divisor;
            }

            CodecFileWriter.WriteAtomically(filePath, VectorCodecFiles.Quantised, durable: false, bodyOutput =>
            {
                Span<float> vector = vectorBuffer.AsSpan(0, dimension);
                ReadOnlySpan<float> writtenCentroid = centroidBuffer.AsSpan(0, dimension);
                bodyOutput.WriteInt32(docCount);
                bodyOutput.WriteInt32(dimension);
                bodyOutput.WriteByte((byte)VectorQuantisation.BBQ);
                bodyOutput.WriteBytes(presence, 0, presence.Length);
                bodyOutput.WriteByte((byte)QuantisedVectorStorageKind.LegacyBbqSign);
                for (int j = 0; j < dimension; j++)
                    bodyOutput.WriteSingle(writtenCentroid[j]);

                for (int docId = 0; docId < docCount; docId++)
                {
                    vectorsByDoc.CopyVectorTo(docId, vector);
                    float correction1 = 0f;
                    float correction2 = 0f;
                    float correction3 = 0f;
                    for (int j = 0; j < dimension; j++)
                    {
                        float residual = vector[j] - writtenCentroid[j];
                        float sign = residual > 0f ? 1f : -1f;
                        correction1 += residual;
                        correction2 += sign * residual;
                        correction3 += residual * residual;
                    }
                    bodyOutput.WriteSingle(correction1);
                    bodyOutput.WriteSingle(correction2);
                    bodyOutput.WriteSingle(correction3);
                }

                for (int docId = 0; docId < docCount; docId++)
                {
                    vectorsByDoc.CopyVectorTo(docId, vector);
                    packedBuffer.AsSpan(0, packedBytes).Clear();
                    for (int j = 0; j < dimension; j++)
                    {
                        if (vector[j] - writtenCentroid[j] <= 0f)
                            continue;
                        packedBuffer[j / 8] |= (byte)(1 << (j % 8));
                    }
                    bodyOutput.WriteBytes(packedBuffer, 0, packedBytes);
                }
            });
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packedBuffer, clearArray: false);
            ArrayPool<float>.Shared.Return(centroidBuffer, clearArray: false);
            ArrayPool<float>.Shared.Return(vectorBuffer, clearArray: false);
        }
    }

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

        CodecFileWriter.WriteAtomically(filePath, VectorCodecFiles.Quantised, durable: false, bodyOutput =>
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
    {
        ArgumentOutOfRangeException.ThrowIfNegative(docCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        if (centroid.Length != dimension)
            throw new ArgumentException($"Centroid dimension {centroid.Length} != {dimension}.", nameof(centroid));

        int packedBytes = checked((dimension + 7) / 8);

        VectorPresence.ValidateVectors(docCount, dimension, vectorsByDoc);
        var presence = VectorPresence.Create(docCount, vectorsByDoc.Keys);
        float[] centroidValues = centroid.ToArray();
        CodecFileWriter.WriteAtomically(filePath, VectorCodecFiles.Quantised, durable: false, bodyOutput =>
        {
        bodyOutput.WriteInt32(docCount);
        bodyOutput.WriteInt32(dimension);
        bodyOutput.WriteByte((byte)VectorQuantisation.BBQ);
        bodyOutput.WriteBytes(presence, 0, presence.Length);
        bodyOutput.WriteByte((byte)QuantisedVectorStorageKind.LegacyBbqSign);

        // Write centroid
        for (int j = 0; j < dimension; j++)
            bodyOutput.WriteSingle(centroidValues[j]);

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
                    float residual = span[j] - centroidValues[j];
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

                bodyOutput.WriteSingle(corr1);
                bodyOutput.WriteSingle(corr2);
                bodyOutput.WriteSingle(corr3);

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
            bodyOutput.WriteBytes(packed, 0, packed.Length);
        });
    }
}

using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Writes quantised dense vectors in the <c>.vq</c> format. The file format is self-describing: the quantisation
/// type is encoded in the header so the reader can validate against segment metadata.
/// </summary>
/// <remarks>
/// File format:
/// <code>
/// [version:byte=5]
/// [docCount:int32][dimension:int32]
/// [quantisation:byte]
/// [vectorCount:int32][docIds:int32[vectorCount]]
/// -- int8 (quantisation=1) --
/// [min:float32][alpha:float32]
/// per vector: [correction:float32]
/// packed: [vectorCount * dimension:byte]
/// -- BBQ (quantisation=2) --
/// [centroid:float32[dimension]]
/// per vector: [correction:float32 * 3]
/// packed: [vectorCount * ceil(dimension/8):byte]
/// -- int4 (quantisation=3) --
/// [min:float32][alpha:float32][l2Error:float32[vectorCount]]
/// packed: [vectorCount * ceil(dimension/2):byte]
/// -- product quantisation (quantisation=4) --
/// [subquantisers:int32][centroids:int32][subspace codebooks]
/// [routingSubquantisers:int32][routing subspace codebooks]
/// [one byte primary code per subquantiser][one byte routing code per routing subquantiser]
/// -- RaBitQ (quantisation=5) --
/// [seed:int64][rotatedDimension:int32][scale+error per vector][packed signs]
/// </code>
/// </remarks>
internal static class QuantisedVectorWriter
{
    private const float Epsilon = 1e-8f;
    // PQ uses 8-bit codewords, as in the conventional PQ layout. One-dimensional
    // subspaces are the quality-selected default for the ADR016 workload.
    // The bounded training sample makes segment flush cost predictable on large
    // segments while the seeded shuffle keeps the encoded output reproducible.
    internal const int DefaultProductCentroidCount = 256;
    internal const int DefaultProductSubspaceDimensions = 1;
    internal const int DefaultProductTrainingSampleSize = 2_048;
    internal const int DefaultProductTrainingIterations = 16;
    internal const int DefaultProductRoutingSubspaceDimensions = 4;
    private const int ProductTrainingSeed = 0x5051;
    private const long RaBitQSeed = 0x5241424954513230;

    internal static void Write(
        string filePath,
        int docCount,
        int dimension,
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> vectorsByDoc,
        VectorQuantisation quantisation,
        ReadOnlySpan<float> bbqCentroid = default)
    {
        switch (quantisation)
        {
            case VectorQuantisation.Int8:
                WriteInt8(filePath, docCount, dimension, vectorsByDoc);
                break;
            case VectorQuantisation.BBQ:
                WriteBBQ(filePath, docCount, dimension, vectorsByDoc, bbqCentroid);
                break;
            case VectorQuantisation.Int4:
                WriteInt4(filePath, docCount, dimension, vectorsByDoc);
                break;
            case VectorQuantisation.ProductQuantisation:
                WriteProductQuantisation(
                    filePath,
                    docCount,
                    dimension,
                    vectorsByDoc,
                    includeRoutingCodebooks: true);
                break;
            case VectorQuantisation.RaBitQ:
                WriteRaBitQ(filePath, docCount, dimension, vectorsByDoc);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(quantisation), quantisation, "A quantised vector encoding is required.");
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
        ArgumentNullException.ThrowIfNull(vectorsByDoc);

        int[] docIds = GetValidatedDocIds(docCount, dimension, vectorsByDoc);

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

        using var output = new IndexOutput(filePath);
        using var scope = CodecFileHeader.BeginStreamingWrite(
            output,
            CodecConstants.QuantisedVectorVersion);
        output.WriteInt32(docCount);
        output.WriteInt32(dimension);
        output.WriteByte((byte)VectorQuantisation.Int8);
        output.WriteInt32(docIds.Length);
        foreach (int docId in docIds)
            output.WriteInt32(docId);
        output.WriteSingle(min);
        output.WriteSingle(alpha);

        // Corrections precede the packed block in the format. Compute them without retaining
        // the packed vectors, then quantise again directly into the output stream.
        foreach (int docId in docIds)
        {
            ReadOnlySpan<float> span = vectorsByDoc[docId].Span;
            float correction = 0f;
            for (int j = 0; j < dimension; j++)
            {
                float orig = span[j];
                byte quantised = QuantiseInt8(orig, min, alpha);
                float reconstructed = min + alpha * quantised;
                float error = orig - reconstructed;
                correction += alpha * quantised * error;
            }
            output.WriteSingle(correction);
        }

        foreach (int docId in docIds)
        {
            ReadOnlySpan<float> span = vectorsByDoc[docId].Span;
            for (int j = 0; j < dimension; j++)
                output.WriteByte(QuantiseInt8(span[j], min, alpha));
        }
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
        ArgumentNullException.ThrowIfNull(vectorsByDoc);
        if (centroid.Length != dimension)
            throw new ArgumentException($"Centroid dimension {centroid.Length} != {dimension}.", nameof(centroid));

        int[] docIds = GetValidatedDocIds(docCount, dimension, vectorsByDoc);
        int packedBytes = (dimension + 7) / 8;

        using var output = new IndexOutput(filePath);
        using var scope = CodecFileHeader.BeginStreamingWrite(
            output,
            CodecConstants.QuantisedVectorVersion);
        output.WriteInt32(docCount);
        output.WriteInt32(dimension);
        output.WriteByte((byte)VectorQuantisation.BBQ);
        output.WriteInt32(docIds.Length);
        foreach (int docId in docIds)
            output.WriteInt32(docId);

        // Write centroid
        for (int j = 0; j < dimension; j++)
            output.WriteSingle(centroid[j]);

        foreach (int docId in docIds)
        {
            ReadOnlySpan<float> span = vectorsByDoc[docId].Span;

            float corr1 = 0f;
            float corr2 = 0f;
            float corr3 = 0f;
            for (int j = 0; j < dimension; j++)
            {
                float residual = span[j] - centroid[j];
                float sign = residual > 0f ? 1f : -1f;
                corr1 += residual;
                corr2 += sign * residual;
                corr3 += residual * residual;
            }
            output.WriteSingle(corr1);
            output.WriteSingle(corr2);
            output.WriteSingle(corr3);
        }

        byte[] bitBuffer = ArrayPool<byte>.Shared.Rent(packedBytes);
        try
        {
            foreach (int docId in docIds)
            {
                ReadOnlySpan<float> span = vectorsByDoc[docId].Span;
                Span<byte> packed = bitBuffer.AsSpan(0, packedBytes);
                packed.Clear();
                for (int j = 0; j < dimension; j++)
                {
                    if (span[j] > centroid[j])
                        packed[j / 8] |= (byte)(1 << (j % 8));
                }
                output.WriteBytes(packed);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bitBuffer, clearArray: false);
        }
    }

    internal static void WriteInt4(
        string filePath,
        int docCount,
        int dimension,
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> vectorsByDoc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(docCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        ArgumentNullException.ThrowIfNull(vectorsByDoc);

        int[] docIds = GetValidatedDocIds(docCount, dimension, vectorsByDoc);
        (float min, float max) = FindRange(vectorsByDoc);
        float alpha = (max - min) / 15f;
        int packedBytes = (dimension + 1) / 2;

        using var output = new IndexOutput(filePath);
        using var scope = CodecFileHeader.BeginStreamingWrite(output, CodecConstants.QuantisedVectorVersion);
        WriteCommonHeader(output, docCount, dimension, VectorQuantisation.Int4, docIds);
        output.WriteSingle(min);
        output.WriteSingle(alpha);

        foreach (int docId in docIds)
        {
            ReadOnlySpan<float> vector = vectorsByDoc[docId].Span;
            float squaredError = 0f;
            for (int j = 0; j < dimension; j++)
            {
                byte code = QuantiseInt4(vector[j], min, alpha);
                float error = vector[j] - (min + alpha * code);
                squaredError += error * error;
            }
            output.WriteSingle(MathF.Sqrt(squaredError));
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(packedBytes);
        try
        {
            foreach (int docId in docIds)
            {
                Span<byte> packed = buffer.AsSpan(0, packedBytes);
                packed.Clear();
                ReadOnlySpan<float> vector = vectorsByDoc[docId].Span;
                for (int j = 0; j < dimension; j++)
                {
                    byte code = QuantiseInt4(vector[j], min, alpha);
                    packed[j >> 1] |= (byte)(code << ((j & 1) * 4));
                }
                output.WriteBytes(packed);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }

    internal static void WriteProductQuantisation(
        string filePath,
        int docCount,
        int dimension,
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> vectorsByDoc,
        int subspaceDimensions = DefaultProductSubspaceDimensions,
        int trainingSampleSize = DefaultProductTrainingSampleSize,
        int trainingIterations = DefaultProductTrainingIterations,
        bool includeRoutingCodebooks = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(docCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        ArgumentNullException.ThrowIfNull(vectorsByDoc);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(subspaceDimensions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(trainingSampleSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(trainingIterations);

        int[] docIds = GetValidatedDocIds(docCount, dimension, vectorsByDoc);
        int subquantiserCount = (dimension + subspaceDimensions - 1) / subspaceDimensions;
        int centroidCount = Math.Max(1, Math.Min(DefaultProductCentroidCount, docIds.Length));
        ProductSubspace[] subspaces = TrainProductCodebooks(
            dimension,
            subquantiserCount,
            centroidCount,
            docIds,
            vectorsByDoc,
            trainingSampleSize,
            trainingIterations);
        ProductSubspace[] routingSubspaces = includeRoutingCodebooks
            ? TrainProductCodebooks(
                dimension,
                (dimension + DefaultProductRoutingSubspaceDimensions - 1) /
                    DefaultProductRoutingSubspaceDimensions,
                centroidCount,
                docIds,
                vectorsByDoc,
                trainingSampleSize,
                trainingIterations)
            : [];

        using var output = new IndexOutput(filePath);
        using var scope = CodecFileHeader.BeginStreamingWrite(output, CodecConstants.QuantisedVectorVersion);
        WriteCommonHeader(
            output, docCount, dimension, VectorQuantisation.ProductQuantisation, docIds);
        output.WriteInt32(subquantiserCount);
        output.WriteInt32(centroidCount);
        foreach (ProductSubspace subspace in subspaces)
        {
            output.WriteInt32(subspace.Start);
            output.WriteInt32(subspace.Length);
            foreach (float value in subspace.Centroids)
                output.WriteSingle(value);
        }
        output.WriteInt32(routingSubspaces.Length);
        foreach (ProductSubspace subspace in routingSubspaces)
        {
            output.WriteInt32(subspace.Start);
            output.WriteInt32(subspace.Length);
            foreach (float value in subspace.Centroids)
                output.WriteSingle(value);
        }

        foreach (int docId in docIds)
        {
            ReadOnlySpan<float> vector = vectorsByDoc[docId].Span;
            foreach (ProductSubspace subspace in subspaces)
                output.WriteByte(FindNearestCentroid(vector, subspace, centroidCount));
        }
        foreach (int docId in docIds)
        {
            ReadOnlySpan<float> vector = vectorsByDoc[docId].Span;
            foreach (ProductSubspace subspace in routingSubspaces)
                output.WriteByte(FindNearestCentroid(vector, subspace, centroidCount));
        }
    }

    internal static void WriteRaBitQ(
        string filePath,
        int docCount,
        int dimension,
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> vectorsByDoc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(docCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        ArgumentNullException.ThrowIfNull(vectorsByDoc);

        int[] docIds = GetValidatedDocIds(docCount, dimension, vectorsByDoc);
        int rotatedDimension = NextPowerOfTwo(dimension);
        int packedBytes = (rotatedDimension + 7) / 8;
        var scales = new float[docIds.Length];
        var errors = new float[docIds.Length];

        for (int ordinal = 0; ordinal < docIds.Length; ordinal++)
        {
            float[] rotated = Rotate(vectorsByDoc[docIds[ordinal]].Span, rotatedDimension);
            float scale = 0f;
            for (int j = 0; j < rotated.Length; j++)
                scale += MathF.Abs(rotated[j]);
            scale /= rotated.Length;
            scales[ordinal] = scale;

            var reconstructedRotated = new float[rotatedDimension];
            for (int j = 0; j < rotatedDimension; j++)
                reconstructedRotated[j] = rotated[j] >= 0f ? scale : -scale;
            InverseRotateInPlace(reconstructedRotated);

            ReadOnlySpan<float> original = vectorsByDoc[docIds[ordinal]].Span;
            float squaredError = 0f;
            for (int j = 0; j < dimension; j++)
            {
                float error = original[j] - reconstructedRotated[j];
                squaredError += error * error;
            }
            errors[ordinal] = MathF.Sqrt(squaredError);
        }

        using var output = new IndexOutput(filePath);
        using var scope = CodecFileHeader.BeginStreamingWrite(output, CodecConstants.QuantisedVectorVersion);
        WriteCommonHeader(output, docCount, dimension, VectorQuantisation.RaBitQ, docIds);
        output.WriteInt64(RaBitQSeed);
        output.WriteInt32(rotatedDimension);
        for (int ordinal = 0; ordinal < docIds.Length; ordinal++)
        {
            output.WriteSingle(scales[ordinal]);
            output.WriteSingle(errors[ordinal]);
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(packedBytes);
        try
        {
            foreach (int docId in docIds)
            {
                Span<byte> packed = buffer.AsSpan(0, packedBytes);
                packed.Clear();
                float[] rotated = Rotate(vectorsByDoc[docId].Span, rotatedDimension);
                for (int j = 0; j < rotatedDimension; j++)
                {
                    if (rotated[j] >= 0f)
                        packed[j >> 3] |= (byte)(1 << (j & 7));
                }
                output.WriteBytes(packed);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }

    private static byte QuantiseInt8(float value, float min, float alpha) =>
        (byte)Math.Clamp((value - min) / alpha + 0.5f, 0f, 255f);

    private static byte QuantiseInt4(float value, float min, float alpha) =>
        (byte)Math.Clamp((value - min) / alpha + 0.5f, 0f, 15f);

    private static (float Min, float Max) FindRange(
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> vectorsByDoc)
    {
        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (ReadOnlyMemory<float> memory in vectorsByDoc.Values)
        {
            foreach (float value in memory.Span)
            {
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
        }
        if (min == float.MaxValue)
            return (0f, 1f);
        if (Math.Abs(max - min) < Epsilon)
            max = min + 1f;
        return (min, max);
    }

    private static void WriteCommonHeader(
        IndexOutput output,
        int docCount,
        int dimension,
        VectorQuantisation quantisation,
        int[] docIds)
    {
        output.WriteInt32(docCount);
        output.WriteInt32(dimension);
        output.WriteByte((byte)quantisation);
        output.WriteInt32(docIds.Length);
        foreach (int docId in docIds)
            output.WriteInt32(docId);
    }

    private static ProductSubspace[] TrainProductCodebooks(
        int dimension,
        int subquantiserCount,
        int centroidCount,
        int[] docIds,
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> vectorsByDoc,
        int trainingSampleSize,
        int trainingIterations)
    {
        var result = new ProductSubspace[subquantiserCount];
        int[] trainingDocIds = SampleTrainingDocIds(docIds, trainingSampleSize);
        for (int sub = 0; sub < subquantiserCount; sub++)
        {
            int start = sub * dimension / subquantiserCount;
            int end = (sub + 1) * dimension / subquantiserCount;
            int length = end - start;
            var centroids = new float[centroidCount * length];

            if (trainingDocIds.Length > 0)
            {
                for (int centroid = 0; centroid < centroidCount; centroid++)
                {
                    int sourceOrdinal = centroid * trainingDocIds.Length / centroidCount;
                    vectorsByDoc[trainingDocIds[sourceOrdinal]].Span.Slice(start, length)
                        .CopyTo(centroids.AsSpan(centroid * length, length));
                }

                var sums = new float[centroids.Length];
                var counts = new int[centroidCount];
                for (int iteration = 0; iteration < trainingIterations; iteration++)
                {
                    Array.Clear(sums);
                    Array.Clear(counts);
                    var subspace = new ProductSubspace(start, length, centroids);
                    for (int ordinal = 0; ordinal < trainingDocIds.Length; ordinal++)
                    {
                        byte nearest = FindNearestCentroid(
                            vectorsByDoc[trainingDocIds[ordinal]].Span, subspace, centroidCount);
                        counts[nearest]++;
                        ReadOnlySpan<float> vector = vectorsByDoc[trainingDocIds[ordinal]].Span;
                        for (int j = 0; j < length; j++)
                            sums[nearest * length + j] += vector[start + j];
                    }
                    for (int centroid = 0; centroid < centroidCount; centroid++)
                    {
                        if (counts[centroid] == 0)
                            continue;
                        for (int j = 0; j < length; j++)
                            centroids[centroid * length + j] =
                                sums[centroid * length + j] / counts[centroid];
                    }
                }
            }
            result[sub] = new ProductSubspace(start, length, centroids);
        }
        return result;
    }

    private static int[] SampleTrainingDocIds(int[] docIds, int trainingSampleSize)
    {
        int count = Math.Min(trainingSampleSize, docIds.Length);
        if (count == 0)
            return [];

        var shuffled = docIds.ToArray();
        var random = new Random(ProductTrainingSeed);
        for (int i = shuffled.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        if (count == shuffled.Length)
            return shuffled;
        return shuffled.AsSpan(0, count).ToArray();
    }

    private static byte FindNearestCentroid(
        ReadOnlySpan<float> vector,
        ProductSubspace subspace,
        int centroidCount)
    {
        int nearest = 0;
        float bestDistance = float.PositiveInfinity;
        for (int centroid = 0; centroid < centroidCount; centroid++)
        {
            float distance = 0f;
            int centroidOffset = centroid * subspace.Length;
            for (int j = 0; j < subspace.Length; j++)
            {
                float delta = vector[subspace.Start + j] -
                    subspace.Centroids[centroidOffset + j];
                distance += delta * delta;
            }
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = centroid;
            }
        }
        return (byte)nearest;
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
            result = checked(result << 1);
        return result;
    }

    private static float[] Rotate(ReadOnlySpan<float> vector, int rotatedDimension)
    {
        var result = new float[rotatedDimension];
        vector.CopyTo(result);
        ApplyRandomSigns(result);
        HadamardInPlace(result);
        float factor = 1f / MathF.Sqrt(rotatedDimension);
        for (int i = 0; i < result.Length; i++)
            result[i] *= factor;
        return result;
    }

    private static void InverseRotateInPlace(Span<float> vector)
    {
        HadamardInPlace(vector);
        float factor = 1f / MathF.Sqrt(vector.Length);
        for (int i = 0; i < vector.Length; i++)
            vector[i] *= factor;
        ApplyRandomSigns(vector);
    }

    private static void HadamardInPlace(Span<float> values)
    {
        for (int width = 1; width < values.Length; width <<= 1)
        {
            for (int start = 0; start < values.Length; start += width << 1)
            {
                for (int offset = 0; offset < width; offset++)
                {
                    float left = values[start + offset];
                    float right = values[start + offset + width];
                    values[start + offset] = left + right;
                    values[start + offset + width] = left - right;
                }
            }
        }
    }

    private static void ApplyRandomSigns(Span<float> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            ulong mixed = Mix64(unchecked((ulong)RaBitQSeed) + (ulong)i);
            if ((mixed & 1) != 0)
                values[i] = -values[i];
        }
    }

    private static ulong Mix64(ulong value)
    {
        value ^= value >> 30;
        value *= 0xbf58476d1ce4e5b9UL;
        value ^= value >> 27;
        value *= 0x94d049bb133111ebUL;
        return value ^ (value >> 31);
    }

    private sealed record ProductSubspace(int Start, int Length, float[] Centroids);

    private static int[] GetValidatedDocIds(
        int docCount,
        int dimension,
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> vectorsByDoc)
    {
        int[] docIds = vectorsByDoc.Keys.Order().ToArray();
        foreach (int docId in docIds)
        {
            if ((uint)docId >= (uint)docCount)
                throw new InvalidDataException(
                    $"Vector document identifier {docId} is outside the segment range 0..{docCount - 1}.");
            int actualDimension = vectorsByDoc[docId].Length;
            if (actualDimension != dimension)
                throw new InvalidDataException(
                    $"Vector for document {docId} has dimension {actualDimension}; expected {dimension}.");
            foreach (float value in vectorsByDoc[docId].Span)
            {
                if (!float.IsFinite(value))
                    throw new InvalidDataException(
                        $"Vector for document {docId} contains a non-finite value.");
            }
        }
        return docIds;
    }
}

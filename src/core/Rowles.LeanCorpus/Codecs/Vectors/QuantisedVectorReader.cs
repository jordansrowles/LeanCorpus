using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Reads quantised vectors from a <c>.vq</c> file written by <see cref="QuantisedVectorWriter"/>.
/// Uses memory-mapped I/O for zero-copy access. Dequantisation produces float arrays suitable
/// for HNSW distance computation.
/// </summary>
internal sealed class QuantisedVectorReader : IDisposable
{
    private readonly IndexInput _input;
    private readonly int _docCount;
    private readonly int _vectorCount;
    private readonly int _dimension;
    private readonly int[]? _docToOrdinal;
    private readonly VectorQuantisation _quantisation;
    private readonly long _packedStart;
    private readonly long _correctionStart;

    // Int8 parameters
    private readonly float _min;
    private readonly float _alpha;

    // BBQ parameters
    private readonly float[]? _centroid;
    private readonly int _bbqPackedBytes;
    private readonly int _int4PackedBytes;

    // Product quantisation parameters
    private readonly int _productCentroidCount;
    private readonly ProductSubspace[]? _productSubspaces;
    private readonly ProductSubspace[]? _productRoutingSubspaces;
    private readonly long _productRoutingPackedStart;

    // RaBitQ parameters
    private readonly long _raBitQSeed;
    private readonly int _raBitQDimension;
    private readonly int _raBitQPackedBytes;

    private bool _disposed;

    private QuantisedVectorReader(
        IndexInput input,
        int docCount,
        int vectorCount,
        int dimension,
        int[]? docToOrdinal,
        VectorQuantisation quantisation,
        long correctionStart,
        long packedStart,
        float min,
        float alpha,
        float[]? centroid,
        int productCentroidCount,
        ProductSubspace[]? productSubspaces,
        ProductSubspace[]? productRoutingSubspaces,
        long productRoutingPackedStart,
        long raBitQSeed,
        int raBitQDimension)
    {
        _input = input;
        _docCount = docCount;
        _vectorCount = vectorCount;
        _dimension = dimension;
        _docToOrdinal = docToOrdinal;
        _quantisation = quantisation;
        _correctionStart = correctionStart;
        _packedStart = packedStart;
        _min = min;
        _alpha = alpha;
        _centroid = centroid;
        _bbqPackedBytes = (dimension + 7) / 8;
        _int4PackedBytes = (dimension + 1) / 2;
        _productCentroidCount = productCentroidCount;
        _productSubspaces = productSubspaces;
        _productRoutingSubspaces = productRoutingSubspaces;
        _productRoutingPackedStart = productRoutingPackedStart;
        _raBitQSeed = raBitQSeed;
        _raBitQDimension = raBitQDimension;
        _raBitQPackedBytes = (raBitQDimension + 7) / 8;
    }

    public static QuantisedVectorReader Open(string filePath)
    {
        var input = new IndexInput(filePath);
        return Open(input);
    }

    /// <summary>Opens a quantised vector reader over a caller-provided Store input and assumes ownership.</summary>
    internal static QuantisedVectorReader Open(IndexInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            byte version = CodecFileHeader.ReadVersion(input, CodecFormats.QuantisedVectors);

            if (version > CodecConstants.QuantisedVectorVersion)
                throw new InvalidDataException(
                    $"Unsupported quantised vector format version {version}. " +
                    $"This build supports up to version {CodecConstants.QuantisedVectorVersion}.");

            long offset = input.Position;

            int docCount = input.ReadInt32(ref offset);
            int dimension = input.ReadInt32(ref offset);
            var quantisation = (VectorQuantisation)input.ReadByte(ref offset);

            int vectorCount = docCount;
            int[]? docToOrdinal = null;
            if (version >= 2)
            {
                vectorCount = input.ReadInt32(ref offset);
                ValidateCounts(docCount, vectorCount, dimension);
                docToOrdinal = new int[docCount];
                Array.Fill(docToOrdinal, -1);
                for (int ordinal = 0; ordinal < vectorCount; ordinal++)
                {
                    int docId = input.ReadInt32(ref offset);
                    if ((uint)docId >= (uint)docCount)
                        throw new InvalidDataException(
                            $"Quantised vector document identifier {docId} is outside the range 0..{docCount - 1}.");
                    if (docToOrdinal[docId] != -1)
                        throw new InvalidDataException(
                            $"Quantised vector document identifier {docId} is duplicated.");
                    docToOrdinal[docId] = ordinal;
                }
            }
            else
            {
                ValidateCounts(docCount, vectorCount, dimension);
            }

            float min = 0f, alpha = 0f;
            float[]? centroid = null;
            int productCentroidCount = 0;
            ProductSubspace[]? productSubspaces = null;
            ProductSubspace[]? productRoutingSubspaces = null;
            long raBitQSeed = 0;
            int raBitQDimension = 0;

            if (version < 3 && quantisation > VectorQuantisation.BBQ)
                throw new InvalidDataException(
                    $"Quantisation type {quantisation} requires .vq format version 3.");

            switch (quantisation)
            {
                case VectorQuantisation.Int8:
                    min = input.ReadSingle(ref offset);
                    alpha = input.ReadSingle(ref offset);
                    ValidateScalarParameters(min, alpha, quantisation);
                    break;

                case VectorQuantisation.BBQ:
                    centroid = new float[dimension];
                    for (int j = 0; j < dimension; j++)
                    {
                        centroid[j] = input.ReadSingle(ref offset);
                        if (!float.IsFinite(centroid[j]))
                            throw new InvalidDataException(
                                $"BBQ centroid contains a non-finite value at dimension {j}.");
                    }
                    break;

                case VectorQuantisation.Int4:
                    min = input.ReadSingle(ref offset);
                    alpha = input.ReadSingle(ref offset);
                    ValidateScalarParameters(min, alpha, quantisation);
                    break;

                case VectorQuantisation.ProductQuantisation:
                    int subquantiserCount = input.ReadInt32(ref offset);
                    productCentroidCount = input.ReadInt32(ref offset);
                    if (subquantiserCount <= 0 || subquantiserCount > dimension)
                        throw new InvalidDataException(
                            $"Product quantisation subquantiser count {subquantiserCount} is invalid for dimension {dimension}.");
                    if (productCentroidCount <= 0 || productCentroidCount > 256)
                        throw new InvalidDataException(
                            $"Product quantisation centroid count {productCentroidCount} is outside 1..256.");
                    productSubspaces = new ProductSubspace[subquantiserCount];
                    int expectedStart = 0;
                    for (int sub = 0; sub < subquantiserCount; sub++)
                    {
                        int start = input.ReadInt32(ref offset);
                        int length = input.ReadInt32(ref offset);
                        if (start != expectedStart || length <= 0 ||
                            start > dimension - length)
                        {
                            throw new InvalidDataException(
                                $"Product quantisation subspace {sub} has invalid range {start}..{start + length}.");
                        }
                        var codebook = new float[checked(productCentroidCount * length)];
                        for (int i = 0; i < codebook.Length; i++)
                        {
                            codebook[i] = input.ReadSingle(ref offset);
                            if (!float.IsFinite(codebook[i]))
                                throw new InvalidDataException(
                                    $"Product quantisation codebook {sub} contains a non-finite value.");
                        }
                        productSubspaces[sub] = new ProductSubspace(start, length, codebook);
                        expectedStart += length;
                    }
                    if (expectedStart != dimension)
                        throw new InvalidDataException(
                            $"Product quantisation subspaces cover {expectedStart} dimensions; expected {dimension}.");
                    if (version >= 5)
                    {
                        int routingSubquantiserCount = input.ReadInt32(ref offset);
                        if (routingSubquantiserCount < 0 || routingSubquantiserCount > dimension)
                        {
                            throw new InvalidDataException(
                                $"Product quantisation routing subquantiser count {routingSubquantiserCount} is invalid for dimension {dimension}.");
                        }
                        if (routingSubquantiserCount > 0)
                        {
                            productRoutingSubspaces = ReadProductSubspaces(
                                input,
                                ref offset,
                                routingSubquantiserCount,
                                productCentroidCount,
                                dimension,
                                "routing ");
                        }
                    }
                    break;

                case VectorQuantisation.RaBitQ:
                    raBitQSeed = input.ReadInt64(ref offset);
                    raBitQDimension = input.ReadInt32(ref offset);
                    if (raBitQDimension != NextPowerOfTwo(dimension))
                    {
                        throw new InvalidDataException(
                            $"RaBitQ rotated dimension {raBitQDimension} is invalid for dimension {dimension}.");
                    }
                    break;

                default:
                    throw new InvalidDataException(
                        $"Unsupported quantisation type {quantisation} in .vq file.");
            }

            long correctionStart = offset;
            int correctionSize = quantisation switch
            {
                VectorQuantisation.Int8 or VectorQuantisation.Int4 => 1,
                VectorQuantisation.BBQ => 3,
                VectorQuantisation.RaBitQ => 2,
                VectorQuantisation.ProductQuantisation => 0,
                _ => throw new InvalidDataException($"Unsupported quantisation type {quantisation}."),
            };
            long packedStart = checked(offset + (long)vectorCount * correctionSize * sizeof(float));
            long productRoutingPackedStart = quantisation == VectorQuantisation.ProductQuantisation &&
                productSubspaces is not null
                ? checked(packedStart + (long)vectorCount * productSubspaces.Length)
                : 0;

            return new QuantisedVectorReader(
                input,
                docCount,
                vectorCount,
                dimension,
                docToOrdinal,
                quantisation,
                correctionStart,
                packedStart,
                min,
                alpha,
                centroid,
                productCentroidCount,
                productSubspaces,
                productRoutingSubspaces,
                productRoutingPackedStart,
                raBitQSeed,
                raBitQDimension);
        }
        catch
        {
            input.Dispose();
            throw;
        }
    }

    public int DocCount => _docCount;
    public int VectorCount => _vectorCount;
    public int Dimension => _dimension;
    public VectorQuantisation Quantisation => _quantisation;
    internal int ProductSubquantiserCount => _productSubspaces?.Length ?? 0;
    internal int ProductRoutingSubquantiserCount => _productRoutingSubspaces?.Length ?? 0;

    /// <summary>Dequantises the vector for the given document into a caller-owned buffer.</summary>
    public void ReadVector(int docId, Span<float> destination)
    {
        int ordinal = GetRequiredOrdinal(docId);
        if (destination.Length < _dimension)
            throw new ArgumentException(
                $"Destination length {destination.Length} is smaller than vector dimension {_dimension}.",
                nameof(destination));

        switch (_quantisation)
        {
            case VectorQuantisation.Int8:
                DequantiseInt8(ordinal, destination);
                break;
            case VectorQuantisation.BBQ:
                DequantiseBBQ(ordinal, destination);
                break;
            case VectorQuantisation.Int4:
                DequantiseInt4(ordinal, destination);
                break;
            case VectorQuantisation.ProductQuantisation:
                DequantiseProductQuantisation(ordinal, destination);
                break;
            case VectorQuantisation.RaBitQ:
                DequantiseRaBitQ(ordinal, destination);
                break;
            default:
                throw new InvalidOperationException($"Unknown quantisation: {_quantisation}");
        }
    }

    /// <summary>Allocates and returns a dequantised float array for the given document.</summary>
    public float[]? ReadVector(int docId)
    {
        if (!TryGetOrdinal(docId, out _))
            return null;
        var vec = new float[_dimension];
        ReadVector(docId, vec);
        return vec;
    }

    /// <summary>Returns raw int8 bytes for fused distance computation without dequantisation.</summary>
    public ReadOnlySpan<byte> GetRawInt8Vector(int docId)
    {
        if (_quantisation != VectorQuantisation.Int8)
            throw new InvalidOperationException("GetRawInt8Vector is only valid for Int8 quantisation.");
        int ordinal = GetRequiredOrdinal(docId);

        long position = _packedStart + (long)ordinal * _dimension;
        return _input.ReadSpan(_dimension, ref position);
    }

    /// <summary>Returns raw bit-packed bytes for BBQ distance computation.</summary>
    public ReadOnlySpan<byte> GetRawBBQVector(int docId)
    {
        if (_quantisation != VectorQuantisation.BBQ)
            throw new InvalidOperationException("GetRawBBQVector is only valid for BBQ quantisation.");
        int ordinal = GetRequiredOrdinal(docId);

        long position = _packedStart + (long)ordinal * _bbqPackedBytes;
        return _input.ReadSpan(_bbqPackedBytes, ref position);
    }

    /// <summary>Returns one byte per product subquantiser for code-native scoring.</summary>
    internal ReadOnlySpan<byte> GetRawProductCodes(int docId)
    {
        if (_quantisation != VectorQuantisation.ProductQuantisation)
            throw new InvalidOperationException("Product codes are only available for product quantisation.");
        int ordinal = GetRequiredOrdinal(docId);
        return GetRawProductCodesAtOrdinal(ordinal);
    }

    private ReadOnlySpan<byte> GetRawProductCodesAtOrdinal(int ordinal)
    {
        return GetRawProductCodesAtOrdinal(
            ordinal,
            _productSubspaces!,
            _packedStart,
            "Product");
    }

    /// <summary>Returns routing codes when present, otherwise the primary product codes.</summary>
    internal ReadOnlySpan<byte> GetRawProductRoutingCodes(int docId)
    {
        if (_quantisation != VectorQuantisation.ProductQuantisation)
            throw new InvalidOperationException("Product codes are only available for product quantisation.");
        int ordinal = GetRequiredOrdinal(docId);
        ProductSubspace[] subspaces = _productRoutingSubspaces ?? _productSubspaces!;
        long packedStart = _productRoutingSubspaces is null
            ? _packedStart
            : _productRoutingPackedStart;
        return GetRawProductCodesAtOrdinal(ordinal, subspaces, packedStart, "Product routing");
    }

    private ReadOnlySpan<byte> GetRawProductCodesAtOrdinal(
        int ordinal,
        ProductSubspace[] subspaces,
        long packedStart,
        string codeKind)
    {
        long position = packedStart + (long)ordinal * subspaces.Length;
        ReadOnlySpan<byte> codes = _input.ReadSpan(subspaces.Length, ref position);
        for (int sub = 0; sub < codes.Length; sub++)
        {
            if (codes[sub] >= _productCentroidCount)
                throw new InvalidDataException(
                    $"{codeKind} quantisation code {codes[sub]} is outside the codebook for subspace {sub}.");
        }
        return codes;
    }

    /// <summary>Builds a single asymmetric distance table for a product-quantised query.</summary>
    internal ProductQuantisationQuery PrepareProductQuery(
        ReadOnlySpan<float> query,
        VectorSimilarityFunction similarity,
        bool normalised)
    {
        if (_quantisation != VectorQuantisation.ProductQuantisation)
            throw new InvalidOperationException("Product query preparation requires product quantisation.");
        if (query.Length != _dimension)
            throw new ArgumentException("Query dimension does not match the product codebook.", nameof(query));
        if (similarity == VectorSimilarityFunction.Hamming ||
            (similarity == VectorSimilarityFunction.Cosine && !normalised))
        {
            throw new NotSupportedException(
                "Code-native product quantisation requires normalised cosine, dot product, maximum inner product, or Euclidean similarity.");
        }

        bool dotProduct = similarity is VectorSimilarityFunction.Cosine or
            VectorSimilarityFunction.DotProduct or VectorSimilarityFunction.MaximumInnerProduct;
        ProductSubspace[] subspaces = _productRoutingSubspaces ?? _productSubspaces!;
        if (subspaces.All(static subspace => subspace.Length == 1))
            return new ProductQuantisationQuery(this, query, dotProduct);

        int lookupLength = checked(subspaces.Length * _productCentroidCount);
        float[] lookup = System.Buffers.ArrayPool<float>.Shared.Rent(lookupLength);
        for (int sub = 0; sub < subspaces.Length; sub++)
        {
            ProductSubspace subspace = subspaces[sub];
            for (int centroid = 0; centroid < _productCentroidCount; centroid++)
            {
                float value = 0f;
                int offset = centroid * subspace.Length;
                for (int dimension = 0; dimension < subspace.Length; dimension++)
                {
                    float centre = subspace.Codebook[offset + dimension];
                    value += dotProduct
                        ? query[subspace.Start + dimension] * centre
                        : (query[subspace.Start + dimension] - centre) *
                          (query[subspace.Start + dimension] - centre);
                }
                lookup[sub * _productCentroidCount + centroid] = value;
            }
        }
        return new ProductQuantisationQuery(
            this,
            lookup,
            _productCentroidCount,
            dotProduct,
            pooledLookup: true);
    }

    /// <summary>Computes direct query distance for one-dimensional PQ subspaces.</summary>
    internal float ProductQueryDistance(
        ReadOnlySpan<float> query,
        int docId,
        bool dotProduct)
    {
        ReadOnlySpan<byte> codes = GetRawProductRoutingCodes(docId);
        ProductSubspace[] subspaces = _productRoutingSubspaces ?? _productSubspaces!;
        float value = 0f;
        for (int sub = 0; sub < codes.Length; sub++)
        {
            ProductSubspace subspace = subspaces[sub];
            float centre = subspace.Codebook[codes[sub]];
            float queryValue = query[subspace.Start];
            value += dotProduct
                ? queryValue * centre
                : (queryValue - centre) * (queryValue - centre);
        }
        return dotProduct ? -value : value;
    }

    /// <summary>Computes a code-native distance between two product-quantised vectors.</summary>
    internal float ProductDistance(
        int leftDocId,
        int rightDocId,
        VectorSimilarityFunction similarity,
        bool normalised)
    {
        if (_quantisation != VectorQuantisation.ProductQuantisation)
            throw new InvalidOperationException("Product distance requires product quantisation.");
        if (similarity == VectorSimilarityFunction.Hamming ||
            (similarity == VectorSimilarityFunction.Cosine && !normalised))
        {
            throw new NotSupportedException("This similarity cannot use code-native product quantisation.");
        }

        bool dotProduct = similarity is VectorSimilarityFunction.Cosine or
            VectorSimilarityFunction.DotProduct or VectorSimilarityFunction.MaximumInnerProduct;
        ReadOnlySpan<byte> left = GetRawProductRoutingCodes(leftDocId);
        ReadOnlySpan<byte> right = GetRawProductRoutingCodes(rightDocId);
        ProductSubspace[] subspaces = _productRoutingSubspaces ?? _productSubspaces!;
        float value = 0f;
        for (int sub = 0; sub < left.Length; sub++)
        {
            ProductSubspace subspace = subspaces[sub];
            int leftOffset = left[sub] * subspace.Length;
            int rightOffset = right[sub] * subspace.Length;
            for (int dimension = 0; dimension < subspace.Length; dimension++)
            {
                float a = subspace.Codebook[leftOffset + dimension];
                float b = subspace.Codebook[rightOffset + dimension];
                value += dotProduct ? a * b : (a - b) * (a - b);
            }
        }
        return dotProduct ? -value : value;
    }

    /// <summary>Returns the BBQ centroid, or throws for non-BBQ quantisation.</summary>
    public ReadOnlySpan<float> Centroid
    {
        get
        {
            if (_quantisation != VectorQuantisation.BBQ)
                throw new InvalidOperationException("Centroid is only available for BBQ quantisation.");
            return _centroid!;
        }
    }

    /// <summary>Returns the correction values for the given document.</summary>
    public (float C1, float C2, float C3) GetBBQCorrections(int docId)
    {
        if (_quantisation != VectorQuantisation.BBQ)
            throw new InvalidOperationException("Corrections are only meaningful for BBQ quantisation.");
        int ordinal = GetRequiredOrdinal(docId);

        long position = _correctionStart + (long)ordinal * 3 * sizeof(float);
        float c1 = _input.ReadSingle(ref position);
        float c2 = _input.ReadSingle(ref position);
        float c3 = _input.ReadSingle(ref position);
        return (c1, c2, c3);
    }

    /// <summary>Returns the int8 per-vector correction value.</summary>
    public float GetInt8Correction(int docId)
    {
        if (_quantisation != VectorQuantisation.Int8)
            throw new InvalidOperationException("Int8 correction is only valid for Int8 quantisation.");
        int ordinal = GetRequiredOrdinal(docId);

        long position = _correctionStart + (long)ordinal * sizeof(float);
        float error = _input.ReadSingle(ref position);
        ValidateErrorBound(error, VectorQuantisation.Int4);
        return error;
    }

    /// <summary>Returns the measured L2 reconstruction error for an int4 vector.</summary>
    public float GetInt4ErrorBound(int docId)
    {
        if (_quantisation != VectorQuantisation.Int4)
            throw new InvalidOperationException("The int4 error bound is only valid for Int4 quantisation.");
        int ordinal = GetRequiredOrdinal(docId);
        long position = _correctionStart + (long)ordinal * sizeof(float);
        return _input.ReadSingle(ref position);
    }

    /// <summary>Returns the measured L2 reconstruction error for a RaBitQ vector.</summary>
    public float GetRaBitQErrorBound(int docId)
    {
        if (_quantisation != VectorQuantisation.RaBitQ)
            throw new InvalidOperationException("The RaBitQ error bound is only valid for RaBitQ quantisation.");
        int ordinal = GetRequiredOrdinal(docId);
        long position = _correctionStart + ((long)ordinal * 2 + 1) * sizeof(float);
        float error = _input.ReadSingle(ref position);
        ValidateErrorBound(error, VectorQuantisation.RaBitQ);
        return error;
    }

    /// <summary>Returns the min value used for int8 quantisation.</summary>
    public float Min => _quantisation is VectorQuantisation.Int8 or VectorQuantisation.Int4 ? _min
        : throw new InvalidOperationException("Min is only valid for scalar quantisation.");

    /// <summary>Returns the alpha scale factor used for int8 quantisation.</summary>
    public float Alpha => _quantisation is VectorQuantisation.Int8 or VectorQuantisation.Int4 ? _alpha
        : throw new InvalidOperationException("Alpha is only valid for scalar quantisation.");

    public bool HasVector(int docId) => TryGetOrdinal(docId, out _);

    private void DequantiseInt8(int ordinal, Span<float> destination)
    {
        long position = _packedStart + (long)ordinal * _dimension;
        var packed = _input.ReadSpan(_dimension, ref position);
        for (int j = 0; j < _dimension; j++)
        {
            byte qv = packed[j];
            destination[j] = _min + _alpha * qv;
        }
    }

    private void DequantiseBBQ(int ordinal, Span<float> destination)
    {
        long position = _packedStart + (long)ordinal * _bbqPackedBytes;
        byte[] bits = System.Buffers.ArrayPool<byte>.Shared.Rent(_bbqPackedBytes);
        try
        {
            _input.ReadSpan(_bbqPackedBytes, ref position).CopyTo(bits);

            for (int j = 0; j < _dimension; j++)
            {
                int byteIdx = j / 8;
                int bitIdx = j % 8;
                float sign = ((bits[byteIdx] >> bitIdx) & 1) == 1 ? 1f : -1f;
                destination[j] = _centroid![j] + sign;
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(bits, clearArray: false);
        }
    }

    private void DequantiseInt4(int ordinal, Span<float> destination)
    {
        long errorPosition = _correctionStart + (long)ordinal * sizeof(float);
        ValidateErrorBound(_input.ReadSingle(ref errorPosition), VectorQuantisation.Int4);
        long position = _packedStart + (long)ordinal * _int4PackedBytes;
        ReadOnlySpan<byte> packed = _input.ReadSpan(_int4PackedBytes, ref position);
        for (int j = 0; j < _dimension; j++)
        {
            byte code = (byte)((packed[j >> 1] >> ((j & 1) * 4)) & 0x0f);
            destination[j] = _min + _alpha * code;
        }
    }

    private void DequantiseProductQuantisation(int ordinal, Span<float> destination)
    {
        ReadOnlySpan<byte> codes = GetRawProductCodesAtOrdinal(ordinal);
        ProductSubspace[] subspaces = _productSubspaces
            ?? throw new InvalidOperationException("Product codebooks are not available.");
        for (int sub = 0; sub < subspaces.Length; sub++)
        {
            ProductSubspace subspace = subspaces[sub];
            int code = codes[sub];
            if (code >= _productCentroidCount)
                throw new InvalidDataException(
                    $"Product quantisation code {code} is outside the codebook for subspace {sub}.");
            subspace.Codebook.AsSpan(code * subspace.Length, subspace.Length)
                .CopyTo(destination.Slice(subspace.Start, subspace.Length));
        }
    }


    private void DequantiseRaBitQ(int ordinal, Span<float> destination)
    {
        long correctionPosition = _correctionStart + (long)ordinal * 2 * sizeof(float);
        float scale = _input.ReadSingle(ref correctionPosition);
        float error = _input.ReadSingle(ref correctionPosition);
        if (!float.IsFinite(scale) || scale < 0f)
            throw new InvalidDataException($"RaBitQ vector {ordinal} has invalid scale {scale}.");
        ValidateErrorBound(error, VectorQuantisation.RaBitQ);
        long packedPosition = _packedStart + (long)ordinal * _raBitQPackedBytes;
        ReadOnlySpan<byte> packed = _input.ReadSpan(_raBitQPackedBytes, ref packedPosition);
        var rotated = new float[_raBitQDimension];
        for (int j = 0; j < rotated.Length; j++)
            rotated[j] = ((packed[j >> 3] >> (j & 7)) & 1) != 0 ? scale : -scale;

        HadamardInPlace(rotated);
        float factor = 1f / MathF.Sqrt(rotated.Length);
        for (int j = 0; j < rotated.Length; j++)
            rotated[j] *= factor;
        ApplyRandomSigns(rotated, _raBitQSeed);
        rotated.AsSpan(0, _dimension).CopyTo(destination);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _input.Dispose();
    }

    private bool TryGetOrdinal(int docId, out int ordinal)
    {
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));
        ordinal = _docToOrdinal is null ? docId : _docToOrdinal[docId];
        return ordinal >= 0;
    }

    private int GetRequiredOrdinal(int docId)
    {
        if (!TryGetOrdinal(docId, out int ordinal))
            throw new KeyNotFoundException($"Document {docId} does not have a vector.");
        return ordinal;
    }

    private static void ValidateCounts(int docCount, int vectorCount, int dimension)
    {
        if (docCount < 0)
            throw new InvalidDataException(
                $"Quantised vector file has a negative document count ({docCount}).");
        if (dimension <= 0)
            throw new InvalidDataException(
                $"Quantised vector file has a non-positive dimension ({dimension}).");
        if (vectorCount < 0 || vectorCount > docCount)
            throw new InvalidDataException(
                $"Quantised vector file has vector count {vectorCount} outside the valid range 0..{docCount}.");
    }

    private static void ValidateScalarParameters(
        float min,
        float alpha,
        VectorQuantisation quantisation)
    {
        if (!float.IsFinite(min) || !float.IsFinite(alpha) || alpha <= 0f)
            throw new InvalidDataException(
                $"{quantisation} quantisation has invalid scalar parameters.");
    }

    private static void ValidateErrorBound(float error, VectorQuantisation quantisation)
    {
        if (!float.IsFinite(error) || error < 0f)
            throw new InvalidDataException(
                $"{quantisation} quantisation has invalid reconstruction error {error}.");
    }

    private static ProductSubspace[] ReadProductSubspaces(
        IndexInput input,
        ref long offset,
        int subquantiserCount,
        int centroidCount,
        int dimension,
        string description)
    {
        var subspaces = new ProductSubspace[subquantiserCount];
        int expectedStart = 0;
        for (int sub = 0; sub < subquantiserCount; sub++)
        {
            int start = input.ReadInt32(ref offset);
            int length = input.ReadInt32(ref offset);
            if (start != expectedStart || length <= 0 || start > dimension - length)
            {
                throw new InvalidDataException(
                    $"Product quantisation {description}subspace {sub} has invalid range {start}..{start + length}.");
            }
            var codebook = new float[checked(centroidCount * length)];
            for (int i = 0; i < codebook.Length; i++)
            {
                codebook[i] = input.ReadSingle(ref offset);
                if (!float.IsFinite(codebook[i]))
                {
                    throw new InvalidDataException(
                        $"Product quantisation {description}codebook {sub} contains a non-finite value.");
                }
            }
            subspaces[sub] = new ProductSubspace(start, length, codebook);
            expectedStart += length;
        }
        if (expectedStart != dimension)
        {
            throw new InvalidDataException(
                $"Product quantisation {description}subspaces cover {expectedStart} dimensions; expected {dimension}.");
        }
        return subspaces;
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
            result = checked(result << 1);
        return result;
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

    private static void ApplyRandomSigns(Span<float> values, long seed)
    {
        for (int i = 0; i < values.Length; i++)
        {
            ulong mixed = Mix64(unchecked((ulong)seed) + (ulong)i);
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

    private sealed record ProductSubspace(int Start, int Length, float[] Codebook);
}

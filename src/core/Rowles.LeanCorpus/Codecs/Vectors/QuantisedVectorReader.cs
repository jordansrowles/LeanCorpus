using System.Buffers;
using System.Numerics;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Reads quantised vectors from a <c>.vq</c> file written by <see cref="QuantisedVectorWriter"/>.
/// Uses memory-mapped I/O for zero-copy access. Supports both int8 scalar quantisation
/// and BBQ binary quantisation. Dequantisation produces normalised float arrays suitable
/// for HNSW distance computation.
/// </summary>
internal sealed class QuantisedVectorReader : IDisposable
{
    private readonly IndexInput _input;
    private readonly int _docCount;
    private readonly int _dimension;
    private readonly VectorQuantisation _quantisation;
    private readonly QuantisedVectorStorageKind _storageKind;
    private readonly long _packedStart;
    private readonly long _correctionStart;

    // Int8 parameters
    private readonly float _min;
    private readonly float _alpha;

    // BBQ parameters
    private readonly float[]? _centroid;
    private readonly float _centroidSquaredNorm;
    private readonly int _bbqPackedBytes;
    private readonly long _osqCorrectionsStart;
    private readonly CanonicalVectorEncoding? _canonicalEncoding;
    private readonly CanonicalInt8VectorReader? _canonical;
    private readonly CodecBodyReadSession _frame;

    private bool _disposed;
    private byte[]? _presence;
    internal bool HasPersistedPresence => _presence is not null;
    internal bool HasVector(int docId)
        => (uint)docId < (uint)_docCount && (_presence is null || (_presence[docId >> 3] & (1 << (docId & 7))) != 0);

    private QuantisedVectorReader(
        IndexInput input,
        int docCount,
        int dimension,
        VectorQuantisation quantisation,
        QuantisedVectorStorageKind storageKind,
        long correctionStart,
        long packedStart,
        float min,
        float alpha,
        float[]? centroid,
        float centroidSquaredNorm,
        long osqCorrectionsStart,
        CanonicalVectorEncoding? canonicalEncoding,
        CanonicalInt8VectorReader? canonical,
        CodecBodyReadSession frame)
    {
        _input = input;
        _docCount = docCount;
        _dimension = dimension;
        _quantisation = quantisation;
        _storageKind = storageKind;
        _correctionStart = correctionStart;
        _packedStart = packedStart;
        _min = min;
        _alpha = alpha;
        _centroid = centroid;
        _centroidSquaredNorm = centroidSquaredNorm;
        _bbqPackedBytes = checked((dimension + 7) / 8);
        _osqCorrectionsStart = osqCorrectionsStart;
        _canonicalEncoding = canonicalEncoding;
        _canonical = canonical;
        _frame = frame;
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
        CodecBodyReadSession? frame = null;
        try
        {
            frame = CodecFileReader.OpenSupported(input, VectorCodecFiles.Quantised);

            long offset = input.Position;
            using var reader = input.BeginReadSession();

            int docCount = reader.ReadInt32(ref offset);
            int dimension = reader.ReadInt32(ref offset);
            if (docCount < 0 || dimension <= 0)
                throw new InvalidDataException("Quantised vector file contains an invalid document count or dimension.");

            var quantisation = (VectorQuantisation)reader.ReadByte(ref offset);

            byte[]? presence = frame.FormatVersion >= 2
                ? VectorPresence.Read(input, docCount, ref offset, checked(frame.BodyStart + frame.BodyLength)) : null;
            QuantisedVectorStorageKind storageKind = frame.FormatVersion >= 3
                ? (QuantisedVectorStorageKind)reader.ReadByte(ref offset)
                : quantisation switch
                {
                    VectorQuantisation.Int8 => QuantisedVectorStorageKind.LegacyInt8,
                    VectorQuantisation.BBQ => QuantisedVectorStorageKind.LegacyBbqSign,
                    _ => throw new InvalidDataException($"Unsupported quantisation type {quantisation} in .vq file.")
                };
            if (!Enum.IsDefined(storageKind))
                throw new InvalidDataException($"Unsupported quantised vector storage kind {(byte)storageKind}.");
            if ((storageKind == QuantisedVectorStorageKind.LegacyInt8) != (quantisation == VectorQuantisation.Int8) ||
                (storageKind == QuantisedVectorStorageKind.LegacyBbqSign && quantisation != VectorQuantisation.BBQ) ||
                (storageKind == QuantisedVectorStorageKind.BbqOsqCanonicalInt8 && quantisation != VectorQuantisation.BBQ))
                throw new InvalidDataException("Quantised vector storage kind does not match its quantisation type.");

            long bodyEnd = checked(frame.BodyStart + frame.BodyLength);
            float min = 0f, alpha = 0f;
            float[]? centroid = null;
            float centroidSquaredNorm = 0f;
            long osqCorrectionsStart = 0;
            CanonicalVectorEncoding? canonicalEncoding = null;
            CanonicalInt8VectorReader? canonical = null;

            if (storageKind == QuantisedVectorStorageKind.BbqOsqCanonicalInt8)
            {
                if (frame.FormatVersion != 3 || presence is null)
                    throw new InvalidDataException("BBQ OSQ canonical storage requires a v3 body with persisted presence.");
                if (dimension < 128)
                    throw new InvalidDataException("BBQ OSQ canonical storage requires at least 128 dimensions.");
                canonicalEncoding = (CanonicalVectorEncoding)reader.ReadByte(ref offset);
                if (canonicalEncoding != CanonicalVectorEncoding.PerVectorUnsigned8)
                    throw new InvalidDataException($"Unsupported canonical vector encoding {(byte)canonicalEncoding}.");

                long centroidLength = checked((long)dimension * sizeof(float) + sizeof(float));
                long correctionsLength = checked((long)docCount * 4 * sizeof(float));
                long packedLength = checked((long)docCount * ((dimension + 7L) / 8));
                long canonicalMetadataLength = checked((long)docCount * CanonicalInt8VectorMetadata.Size);
                long canonicalCodesLength = checked((long)docCount * dimension);
                long expectedEnd = checked(offset + centroidLength + correctionsLength + packedLength + canonicalMetadataLength + canonicalCodesLength);
                if (expectedEnd != bodyEnd)
                    throw new InvalidDataException("BBQ OSQ canonical body length does not match its metadata.");

                centroid = new float[dimension];
                for (int index = 0; index < dimension; index++)
                {
                    centroid[index] = reader.ReadSingle(ref offset);
                    if (!float.IsFinite(centroid[index]))
                        throw new InvalidDataException("BBQ OSQ centroid contains a non-finite value.");
                }
                centroidSquaredNorm = reader.ReadSingle(ref offset);
                if (!float.IsFinite(centroidSquaredNorm) || centroidSquaredNorm < 0f)
                    throw new InvalidDataException("BBQ OSQ centroid squared norm is invalid.");
                double measuredCentroidSquaredNorm = 0d;
                foreach (float component in centroid)
                    measuredCentroidSquaredNorm += (double)component * component;
                if (Math.Abs(measuredCentroidSquaredNorm - centroidSquaredNorm) >
                    1e-5d * Math.Max(1d, measuredCentroidSquaredNorm))
                    throw new InvalidDataException("BBQ OSQ centroid squared norm does not match the stored centroid.");

                osqCorrectionsStart = offset;
                long osqPackedStart = checked(osqCorrectionsStart + correctionsLength);
                long canonicalMetadataStart = checked(osqPackedStart + packedLength);
                long canonicalCodesStart = checked(canonicalMetadataStart + canonicalMetadataLength);
                canonical = CanonicalInt8VectorReader.OpenBody(
                    input, docCount, dimension, canonicalMetadataStart, canonicalCodesStart, bodyEnd, presence);

                return new QuantisedVectorReader(input, docCount, dimension,
                    quantisation, storageKind, osqCorrectionsStart, osqPackedStart,
                    min, alpha, centroid, centroidSquaredNorm, osqCorrectionsStart,
                    canonicalEncoding, canonical, frame)
                { _presence = presence };
            }

            long parametersLength = quantisation switch
            {
                VectorQuantisation.Int8 => 2 * sizeof(float),
                VectorQuantisation.BBQ => checked((long)dimension * sizeof(float)),
                _ => throw new InvalidDataException($"Unsupported quantisation type {quantisation} in .vq file.")
            };
            int correctionSize = quantisation == VectorQuantisation.Int8 ? 1 : 3;
            int packedBytes = quantisation == VectorQuantisation.Int8 ? dimension : checked((dimension + 7) / 8);
            long expectedEndLegacy = checked(offset + parametersLength + (long)docCount * (correctionSize * sizeof(float) + (long)packedBytes));
            if (expectedEndLegacy != bodyEnd)
                throw new InvalidDataException("Quantised vector body length does not match its metadata.");

            switch (quantisation)
            {
                case VectorQuantisation.Int8:
                    min = reader.ReadSingle(ref offset);
                    alpha = reader.ReadSingle(ref offset);
                    break;

                case VectorQuantisation.BBQ:
                    centroid = new float[dimension];
                    for (int j = 0; j < dimension; j++)
                        centroid[j] = reader.ReadSingle(ref offset);
                    break;

                default:
                    throw new InvalidDataException(
                        $"Unsupported quantisation type {quantisation} in .vq file.");
            }

            long correctionStart = offset;
            long packedStart = offset + (long)docCount * correctionSize * sizeof(float);
            long dataEnd = checked(packedStart + (long)docCount * packedBytes);
            if (dataEnd != checked(frame.BodyStart + frame.BodyLength))
                throw new InvalidDataException("Quantised vector body length does not match its metadata.");

            return new QuantisedVectorReader(input, docCount, dimension,
                quantisation, storageKind, correctionStart, packedStart, min, alpha, centroid,
                centroidSquaredNorm, osqCorrectionsStart, canonicalEncoding, canonical, frame) { _presence = presence };
        }
        catch
        {
            // The canonical reader borrows this input and must not dispose it separately.
            frame?.Dispose();
            input.Dispose();
            throw;
        }
    }

    public int DocCount => _docCount;
    public int Dimension => _dimension;
    public VectorQuantisation Quantisation => _quantisation;
    internal QuantisedVectorStorageKind StorageKind => _storageKind;

    internal CodecRepresentationIdentity LifecycleIdentity => QuantisedVectorLifecyclePolicy.Identity(_frame.FormatVersion, _storageKind);

    internal CanonicalVectorEncoding? CanonicalEncoding => _canonicalEncoding;

    internal float CentroidSquaredNorm
        => _storageKind == QuantisedVectorStorageKind.BbqOsqCanonicalInt8
            ? _centroidSquaredNorm
            : throw new InvalidOperationException("Centroid squared norm is available only for BBQ OSQ canonical storage.");

    /// <summary>Dequantises the vector for the given document into a caller-owned buffer.</summary>
    public void ReadVector(int docId, Span<float> destination)
    {
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));
        if (destination.Length != _dimension)
            throw new ArgumentException($"Destination length {destination.Length} != vector dimension {_dimension}.", nameof(destination));

        switch (_quantisation)
        {
            case VectorQuantisation.Int8:
                DequantiseInt8(docId, destination);
                break;
            case VectorQuantisation.BBQ:
                if (_storageKind == QuantisedVectorStorageKind.BbqOsqCanonicalInt8)
                    _canonical!.CopyVectorTo(docId, destination);
                else
                    DequantiseBBQ(docId, destination);
                break;
            default:
                throw new InvalidOperationException($"Unknown quantisation: {_quantisation}");
        }
    }

    /// <summary>Allocates and returns a dequantised float array for the given document.</summary>
    public float[] ReadVector(int docId)
    {
        var vec = new float[_dimension];
        ReadVector(docId, vec);
        return vec;
    }

    /// <summary>Returns raw int8 bytes for fused distance computation without dequantisation.</summary>
    public ReadOnlySpan<byte> GetRawInt8Vector(int docId)
    {
        if (_quantisation != VectorQuantisation.Int8)
            throw new InvalidOperationException("GetRawInt8Vector is only valid for Int8 quantisation.");
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));

        long position = _packedStart + (long)docId * _dimension;
        return _input.BorrowSpan(_dimension, ref position);
    }

    /// <summary>Returns raw bit-packed bytes for BBQ distance computation.</summary>
    public ReadOnlySpan<byte> GetRawBBQVector(int docId)
    {
        if (_quantisation != VectorQuantisation.BBQ)
            throw new InvalidOperationException("GetRawBBQVector is only valid for BBQ quantisation.");
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));

        long position = _packedStart + (long)docId * _bbqPackedBytes;
        ReadOnlySpan<byte> packed = _input.BorrowSpan(_bbqPackedBytes, ref position);
        if (_storageKind == QuantisedVectorStorageKind.BbqOsqCanonicalInt8 && (_dimension & 7) != 0)
        {
            int unusedBits = 8 - (_dimension & 7);
            if ((packed[^1] & ((1 << unusedBits) - 1)) != 0)
                throw new InvalidDataException($"BBQ OSQ document {docId} has non-zero padding bits.");
        }
        return packed;
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
        if (_storageKind == QuantisedVectorStorageKind.BbqOsqCanonicalInt8)
            throw new InvalidOperationException("Legacy BBQ corrections are not available for OSQ canonical storage.");
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));

        long position = _correctionStart + (long)docId * 3 * sizeof(float);
        float c1 = _input.ReadSingle(ref position);
        float c2 = _input.ReadSingle(ref position);
        float c3 = _input.ReadSingle(ref position);
        return (c1, c2, c3);
    }

    internal BbqOsqCorrections GetOsqCorrections(int docId)
    {
        if (_storageKind != QuantisedVectorStorageKind.BbqOsqCanonicalInt8)
            throw new InvalidOperationException("OSQ corrections are only available for BBQ OSQ canonical storage.");
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));

        long position = checked(_osqCorrectionsStart + (long)docId * 4 * sizeof(float));
        var corrections = new BbqOsqCorrections(
            _input.ReadSingle(ref position),
            _input.ReadSingle(ref position),
            _input.ReadSingle(ref position),
            _input.ReadInt32(ref position));
        if (!float.IsFinite(corrections.LowerInterval) || !float.IsFinite(corrections.UpperInterval) ||
            !float.IsFinite(corrections.AdditionalCorrection) || corrections.LowerInterval > corrections.UpperInterval ||
            corrections.CodeSum < 0 || corrections.CodeSum > _dimension)
            throw new InvalidDataException($"BBQ OSQ corrections for document {docId} are invalid.");

        ReadOnlySpan<byte> packed = GetRawBBQVector(docId);
        int packedCodeSum = 0;
        foreach (byte value in packed)
            packedCodeSum += BitOperations.PopCount((uint)value);
        if (packedCodeSum != corrections.CodeSum)
            throw new InvalidDataException($"BBQ OSQ code sum for document {docId} does not match its packed bits.");
        return corrections;
    }

    internal CanonicalInt8VectorMetadata GetCanonicalMetadata(int docId)
        => _storageKind == QuantisedVectorStorageKind.BbqOsqCanonicalInt8
            ? _canonical!.GetCanonicalMetadata(docId)
            : throw new InvalidOperationException("Canonical Int8 metadata is only available for BBQ OSQ canonical storage.");

    internal ReadOnlySpan<byte> GetCanonicalCodes(int docId)
        => _storageKind == QuantisedVectorStorageKind.BbqOsqCanonicalInt8
            ? _canonical!.GetCanonicalCodes(docId)
            : throw new InvalidOperationException("Canonical Int8 codes are only available for BBQ OSQ canonical storage.");

    internal void CopyCanonicalRecordTo(int docId, Span<byte> destination)
    {
        if (_storageKind != QuantisedVectorStorageKind.BbqOsqCanonicalInt8)
            throw new InvalidOperationException("Canonical Int8 records are only available for BBQ OSQ canonical storage.");
        _canonical!.CopyCanonicalRecordTo(docId, destination);
    }

    internal PreparedBbqQuery PrepareBbqQuery(ReadOnlySpan<float> query)
    {
        if (_storageKind != QuantisedVectorStorageKind.BbqOsqCanonicalInt8)
            throw new InvalidOperationException("Prepared OSQ queries are only available for BBQ OSQ canonical storage.");
        if (query.Length != _dimension)
            throw new ArgumentException("Query dimension does not match this segment.", nameof(query));

        float[] queryBuffer = ArrayPool<float>.Shared.Rent(_dimension);
        try
        {
            Span<float> normalised = queryBuffer.AsSpan(0, _dimension);
            query.CopyTo(normalised);
            if (!VectorMath.NormaliseInPlace(normalised))
                normalised.Clear();
            return PreparedBbqQuery.Prepare(normalised, _centroid!);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(queryBuffer, clearArray: true);
        }
    }

    internal PreparedBbqQuery PrepareBbqQuery(int documentId)
    {
        if (_storageKind != QuantisedVectorStorageKind.BbqOsqCanonicalInt8)
            throw new InvalidOperationException("Prepared OSQ queries are only available for BBQ OSQ canonical storage.");
        if (!HasVector(documentId))
            throw new ArgumentOutOfRangeException(nameof(documentId), "Graph query documents must have a canonical vector.");

        float[] queryBuffer = ArrayPool<float>.Shared.Rent(_dimension);
        try
        {
            Span<float> normalised = queryBuffer.AsSpan(0, _dimension);
            _canonical!.CopyVectorTo(documentId, normalised);
            if (!VectorMath.NormaliseInPlace(normalised))
                normalised.Clear();
            return PreparedBbqQuery.Prepare(normalised, _centroid!);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(queryBuffer, clearArray: true);
        }
    }

    internal float ScoreBbqCandidate(PreparedBbqQuery query, int docId)
    {
        if (_storageKind != QuantisedVectorStorageKind.BbqOsqCanonicalInt8)
            throw new InvalidOperationException("OSQ candidate scoring is only available for BBQ OSQ canonical storage.");
        return BbqOsqDistanceComputer.ScorePacked(
            query,
            GetRawBBQVector(docId),
            GetOsqCorrections(docId),
            _centroidSquaredNorm);
    }

    /// <summary>Returns the int8 per-vector correction value.</summary>
    public float GetInt8Correction(int docId)
    {
        if (_quantisation != VectorQuantisation.Int8)
            throw new InvalidOperationException("Int8 correction is only valid for Int8 quantisation.");
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));

        long position = _correctionStart + (long)docId * sizeof(float);
        return _input.ReadSingle(ref position);
    }

    /// <summary>Returns the min value used for int8 quantisation.</summary>
    public float Min => _quantisation == VectorQuantisation.Int8 ? _min
        : throw new InvalidOperationException("Min is only valid for Int8 quantisation.");

    /// <summary>Returns the alpha scale factor used for int8 quantisation.</summary>
    public float Alpha => _quantisation == VectorQuantisation.Int8 ? _alpha
        : throw new InvalidOperationException("Alpha is only valid for Int8 quantisation.");

    private void DequantiseInt8(int docId, Span<float> destination)
    {
        long position = _packedStart + (long)docId * _dimension;
        var packed = _input.BorrowSpan(_dimension, ref position);
        for (int j = 0; j < _dimension; j++)
        {
            byte qv = packed[j];
            destination[j] = _min + _alpha * qv;
        }
    }

    private void DequantiseBBQ(int docId, Span<float> destination)
    {
        long position = _packedStart + (long)docId * _bbqPackedBytes;
        byte[] bits = System.Buffers.ArrayPool<byte>.Shared.Rent(_bbqPackedBytes);
        try
        {
            _input.BorrowSpan(_bbqPackedBytes, ref position).CopyTo(bits);

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _canonical?.Dispose();
        _frame.Dispose();
        _input.Dispose();
    }
}

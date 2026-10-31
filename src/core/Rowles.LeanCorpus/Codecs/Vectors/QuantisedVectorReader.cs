using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

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
    private readonly int _bbqPackedBytes;
    private readonly IDisposable _frame;

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
        IDisposable frame)
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
        _bbqPackedBytes = checked((dimension + 7) / 8);
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
            if (storageKind == QuantisedVectorStorageKind.BbqOsqCanonicalInt8)
                throw new InvalidDataException("BBQ OSQ canonical storage is not available in this reader path.");
            float min = 0f, alpha = 0f;
            float[]? centroid = null;

            long parametersLength = quantisation switch
            {
                VectorQuantisation.Int8 => 2 * sizeof(float),
                VectorQuantisation.BBQ => checked((long)dimension * sizeof(float)),
                _ => throw new InvalidDataException($"Unsupported quantisation type {quantisation} in .vq file.")
            };
            int correctionSize = quantisation == VectorQuantisation.Int8 ? 1 : 3;
            int packedBytes = quantisation == VectorQuantisation.Int8 ? dimension : checked((dimension + 7) / 8);
            long expectedEnd = checked(offset + parametersLength + (long)docCount * (correctionSize * sizeof(float) + (long)packedBytes));
            if (expectedEnd != checked(frame.BodyStart + frame.BodyLength))
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
                quantisation, storageKind, correctionStart, packedStart, min, alpha, centroid, frame) { _presence = presence };
        }
        catch
        {
            frame?.Dispose();
            input.Dispose();
            throw;
        }
    }

    public int DocCount => _docCount;
    public int Dimension => _dimension;
    public VectorQuantisation Quantisation => _quantisation;
    internal QuantisedVectorStorageKind StorageKind => _storageKind;

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
        return _input.BorrowSpan(_bbqPackedBytes, ref position);
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
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));

        long position = _correctionStart + (long)docId * 3 * sizeof(float);
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
        _frame.Dispose();
        _input.Dispose();
    }
}

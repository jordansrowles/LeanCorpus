using System.IO.MemoryMappedFiles;
using System.IO;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Reads quantised vectors from a <c>.vq</c> file written by <see cref="QuantisedVectorWriter"/>.
/// Uses memory-mapped I/O for zero-copy access. Supports both int8 scalar quantisation
/// and BBQ binary quantisation. Dequantisation produces normalised float arrays suitable
/// for HNSW distance computation.
/// </summary>
internal sealed class QuantisedVectorReader : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly int _docCount;
    private readonly int _dimension;
    private readonly VectorQuantisation _quantisation;
    private readonly long _packedStart;
    private readonly long _correctionStart;

    // Int8 parameters
    private readonly float _min;
    private readonly float _alpha;

    // BBQ parameters
    private readonly float[]? _centroid;
    private readonly int _bbqPackedBytes;

    private bool _disposed;

    private QuantisedVectorReader(
        MemoryMappedFile mmf,
        MemoryMappedViewAccessor accessor,
        int docCount,
        int dimension,
        VectorQuantisation quantisation,
        long correctionStart,
        long packedStart,
        float min,
        float alpha,
        float[]? centroid)
    {
        _mmf = mmf;
        _accessor = accessor;
        _docCount = docCount;
        _dimension = dimension;
        _quantisation = quantisation;
        _correctionStart = correctionStart;
        _packedStart = packedStart;
        _min = min;
        _alpha = alpha;
        _centroid = centroid;
        _bbqPackedBytes = (dimension + 7) / 8;
    }

    public static QuantisedVectorReader Open(string filePath)
    {
        byte version;
        long bodyStart;
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            version = CodecFileHeader.ReadVersion(reader, CodecFormats.QuantisedVectors);
            bodyStart = fs.Position;
        }

        if (version > CodecConstants.QuantisedVectorVersion)
            throw new InvalidDataException(
                $"Unsupported quantised vector format version {version}. " +
                $"This build supports up to version {CodecConstants.QuantisedVectorVersion}.");

        var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        var accessor = mmf.CreateViewAccessor(bodyStart, 0, MemoryMappedFileAccess.Read);

        long offset = 0;

        int docCount = accessor.ReadInt32(offset);
        offset += 4;
        int dimension = accessor.ReadInt32(offset);
        offset += 4;

        var quantisation = (VectorQuantisation)accessor.ReadByte(offset);
        offset += 1;

        float min = 0f, alpha = 0f;
        float[]? centroid = null;

        switch (quantisation)
        {
            case VectorQuantisation.Int8:
                min = accessor.ReadSingle(offset);
                offset += 4;
                alpha = accessor.ReadSingle(offset);
                offset += 4;
                break;

            case VectorQuantisation.BBQ:
                centroid = new float[dimension];
                for (int j = 0; j < dimension; j++)
                {
                    centroid[j] = accessor.ReadSingle(offset);
                    offset += 4;
                }
                break;

            default:
                throw new InvalidDataException(
                    $"Unsupported quantisation type {quantisation} in .vq file.");
        }

        long correctionStart = offset;
        int correctionSize = quantisation == VectorQuantisation.Int8 ? 1 : 3;
        long packedStart = offset + (long)docCount * correctionSize * sizeof(float);

        return new QuantisedVectorReader(mmf, accessor, docCount, dimension,
            quantisation, correctionStart, packedStart, min, alpha, centroid);
    }

    public int DocCount => _docCount;
    public int Dimension => _dimension;
    public VectorQuantisation Quantisation => _quantisation;

    /// <summary>Dequantises the vector for the given document into a caller-owned buffer.</summary>
    public void ReadVector(int docId, Span<float> destination)
    {
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));

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

        long offset = _packedStart + (long)docId * _dimension;
        var buf = new byte[_dimension];
        _accessor.ReadArray(offset, buf, 0, _dimension);
        return buf;
    }

    /// <summary>Returns raw bit-packed bytes for BBQ distance computation.</summary>
    public ReadOnlySpan<byte> GetRawBBQVector(int docId)
    {
        if (_quantisation != VectorQuantisation.BBQ)
            throw new InvalidOperationException("GetRawBBQVector is only valid for BBQ quantisation.");
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));

        long offset = _packedStart + (long)docId * _bbqPackedBytes;
        var buf = new byte[_bbqPackedBytes];
        _accessor.ReadArray(offset, buf, 0, _bbqPackedBytes);
        return buf;
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

        long offset = _correctionStart + (long)docId * 3 * sizeof(float);
        float c1 = _accessor.ReadSingle(offset);
        float c2 = _accessor.ReadSingle(offset + 4);
        float c3 = _accessor.ReadSingle(offset + 8);
        return (c1, c2, c3);
    }

    /// <summary>Returns the int8 per-vector correction value.</summary>
    public float GetInt8Correction(int docId)
    {
        if (_quantisation != VectorQuantisation.Int8)
            throw new InvalidOperationException("Int8 correction is only valid for Int8 quantisation.");
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));

        long offset = _correctionStart + (long)docId * sizeof(float);
        return _accessor.ReadSingle(offset);
    }

    /// <summary>Returns the min value used for int8 quantisation.</summary>
    public float Min => _quantisation == VectorQuantisation.Int8 ? _min
        : throw new InvalidOperationException("Min is only valid for Int8 quantisation.");

    /// <summary>Returns the alpha scale factor used for int8 quantisation.</summary>
    public float Alpha => _quantisation == VectorQuantisation.Int8 ? _alpha
        : throw new InvalidOperationException("Alpha is only valid for Int8 quantisation.");

    private void DequantiseInt8(int docId, Span<float> destination)
    {
        long offset = _packedStart + (long)docId * _dimension;
        for (int j = 0; j < _dimension; j++)
        {
            byte qv = _accessor.ReadByte(offset + j);
            destination[j] = _min + _alpha * qv;
        }
    }

    private void DequantiseBBQ(int docId, Span<float> destination)
    {
        long offset = _packedStart + (long)docId * _bbqPackedBytes;
        byte[] bits = System.Buffers.ArrayPool<byte>.Shared.Rent(_bbqPackedBytes);
        try
        {
            _accessor.ReadArray(offset, bits, 0, _bbqPackedBytes);

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
        _accessor.Dispose();
        _mmf.Dispose();
    }
}

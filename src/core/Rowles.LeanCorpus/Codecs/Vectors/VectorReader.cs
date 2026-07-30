using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;
using System.Runtime.InteropServices;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Reads dense float vectors written by <see cref="VectorWriter"/>.
/// Retains a Store-owned input for zero-copy vector access.
/// </summary>
internal sealed class VectorReader : IDisposable
{
    private readonly IndexInput _input;
    private readonly int _docCount;
    private readonly int _vectorCount;
    private readonly int _dimension;
    private readonly long _dataStart;
    private readonly int[]? _docToOrdinal;
    private readonly bool _int8;
    private readonly float _int8Min;
    private readonly float _int8Alpha;
    private bool _disposed;

    private VectorReader(
        IndexInput input,
        int docCount,
        int vectorCount,
        int dimension,
        long dataStart,
        int[]? docToOrdinal,
        bool int8 = false, float int8Min = 0f, float int8Alpha = 0f)
    {
        _input = input;
        _docCount = docCount;
        _vectorCount = vectorCount;
        _dimension = dimension;
        _dataStart = dataStart;
        _docToOrdinal = docToOrdinal;
        _int8 = int8;
        _int8Min = int8Min;
        _int8Alpha = int8Alpha;
    }

    public static VectorReader Open(string filePath)
    {
        var input = new IndexInput(filePath);
        return Open(input);
    }

    /// <summary>Opens a vector reader over a caller-provided Store input and assumes ownership.</summary>
    internal static VectorReader Open(IndexInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            byte version = CodecFileHeader.ReadVersion(input, CodecFormats.Vectors);

            if (version > CodecConstants.VectorVersion)
                throw new InvalidDataException(
                    $"Unsupported vector format version {version}. " +
                    $"This build supports up to version {CodecConstants.VectorVersion}. " +
                    "Please upgrade LeanCorpus.");

            int docCount = input.ReadInt32();
            int dimension = input.ReadInt32();
            byte format = input.ReadByte();

            int vectorCount = docCount;
            int[]? docToOrdinal = null;
            if (version >= 2)
            {
                vectorCount = input.ReadInt32();
                ValidateCounts(docCount, vectorCount, dimension);
                docToOrdinal = new int[docCount];
                Array.Fill(docToOrdinal, -1);
                for (int ordinal = 0; ordinal < vectorCount; ordinal++)
                {
                    int docId = input.ReadInt32();
                    if ((uint)docId >= (uint)docCount)
                        throw new InvalidDataException(
                            $"Vector document identifier {docId} is outside the range 0..{docCount - 1}.");
                    if (docToOrdinal[docId] != -1)
                        throw new InvalidDataException($"Vector document identifier {docId} is duplicated.");
                    docToOrdinal[docId] = ordinal;
                }
            }
            else
            {
                ValidateCounts(docCount, vectorCount, dimension);
            }

            float int8Min = 0f, int8Alpha = 0f;
            bool isInt8 = format == (byte)VectorQuantisation.Int8;
            if (isInt8)
            {
                int8Min = input.ReadSingle();
                int8Alpha = input.ReadSingle();
            }

            long dataStart = input.Position;

            return new VectorReader(
                input,
                docCount,
                vectorCount,
                dimension,
                dataStart,
                docToOrdinal,
                isInt8,
                int8Min,
                int8Alpha);
        }
        catch
        {
            input.Dispose();
            throw;
        }
    }

    public float[]? ReadVector(int docId)
    {
        int ordinal = GetOrdinal(docId);
        if (ordinal < 0)
            return null;

        var vector = new float[_dimension];
        if (_int8)
        {
            long position = _dataStart + (long)ordinal * _dimension;
            var packed = _input.ReadSpan(_dimension, ref position);
            for (int j = 0; j < _dimension; j++)
                vector[j] = _int8Min + _int8Alpha * packed[j];
        }
        else
        {
            long position = _dataStart + (long)ordinal * _dimension * sizeof(float);
            _input.ReadSingleArray(vector, _dimension, ref position);
        }
        return vector;
    }

    /// <summary>
    /// Returns a zero-copy Float32 block over the mapped vector body. The span is
    /// lifetime-bound to this reader and must not outlive its owning segment lease.
    /// Legacy Int8 vector files retain their reconstructing fallback.
    /// </summary>
    internal ReadOnlySpan<float> GetMappedVectorBlock(int docId)
    {
        int ordinal = GetOrdinal(docId);
        if (ordinal < 0)
            throw new KeyNotFoundException($"Document {docId} does not have a vector.");
        if (_int8)
            return ReadVector(docId)!;

        long position = _dataStart + (long)ordinal * _dimension * sizeof(float);
        ReadOnlySpan<byte> bytes = _input.ReadSpan(_dimension * sizeof(float), ref position);
        return MemoryMarshal.Cast<byte, float>(bytes);
    }

    public int Dimension => _dimension;
    public int VectorCount => _vectorCount;
    public int DocCount => _docCount;

    public bool HasVector(int docId) => GetOrdinal(docId) >= 0;

    private int GetOrdinal(int docId)
    {
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));
        return _docToOrdinal is null ? docId : _docToOrdinal[docId];
    }

    private static void ValidateCounts(int docCount, int vectorCount, int dimension)
    {
        if (docCount < 0)
            throw new InvalidDataException($"Vector file has a negative document count ({docCount}).");
        if (dimension <= 0)
            throw new InvalidDataException($"Vector file has a non-positive dimension ({dimension}).");
        if (vectorCount < 0 || vectorCount > docCount)
            throw new InvalidDataException(
                $"Vector file has vector count {vectorCount} outside the valid range 0..{docCount}.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _input.Dispose();
    }
}

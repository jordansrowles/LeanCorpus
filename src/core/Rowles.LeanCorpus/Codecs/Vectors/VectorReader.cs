using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

using System.IO.MemoryMappedFiles;
namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Reads dense float vectors written by <see cref="VectorWriter"/>.
/// Uses memory-mapped I/O for zero-copy vector access.
/// </summary>
internal sealed class VectorReader : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly int _vectorCount;
    private readonly int _dimension;
    private readonly long _dataStart;
    private readonly bool _int8;
    private readonly float _int8Min;
    private readonly float _int8Alpha;
    private bool _disposed;

    private VectorReader(
        MemoryMappedFile mmf, MemoryMappedViewAccessor accessor,
        int vectorCount, int dimension, long dataStart,
        bool int8 = false, float int8Min = 0f, float int8Alpha = 0f)
    {
        _mmf = mmf;
        _accessor = accessor;
        _vectorCount = vectorCount;
        _dimension = dimension;
        _dataStart = dataStart;
        _int8 = int8;
        _int8Min = int8Min;
        _int8Alpha = int8Alpha;
    }

    public static VectorReader Open(string filePath)
    {
        using var input = new IndexInput(filePath);
        byte version = CodecFileHeader.ReadVersion(input, CodecFormats.Vectors);

        if (version > CodecConstants.VectorVersion)
            throw new InvalidDataException(
                $"Unsupported vector format version {version}. " +
                $"This build supports up to version {CodecConstants.VectorVersion}. " +
                "Please upgrade LeanCorpus.");

        int vectorCount = input.ReadInt32();
        int dimension = input.ReadInt32();

        byte format = input.ReadByte();

        float int8Min = 0f, int8Alpha = 0f;
        bool isInt8 = format == (byte)VectorQuantisation.Int8;
        if (isInt8)
        {
            int8Min = input.ReadSingle();
            int8Alpha = input.ReadSingle();
        }

        long dataStart = input.Position;

        var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        return new VectorReader(mmf, accessor, vectorCount, dimension, dataStart, isInt8, int8Min, int8Alpha);
    }

    public float[] ReadVector(int docId)
    {
        var vector = new float[_dimension];
        if (_int8)
        {
            long offset = _dataStart + (long)docId * _dimension;
            for (int j = 0; j < _dimension; j++)
                vector[j] = _int8Min + _int8Alpha * _accessor.ReadByte(offset + j);
        }
        else
        {
            long offset = _dataStart + (long)docId * _dimension * sizeof(float);
            _accessor.ReadArray(offset, vector, 0, _dimension);
        }
        return vector;
    }

    public int Dimension => _dimension;
    public int VectorCount => _vectorCount;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _accessor.Dispose();
        _mmf.Dispose();
    }
}

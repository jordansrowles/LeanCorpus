using System.IO.MemoryMappedFiles;
namespace Rowles.LeanLucene.Codecs.Vectors;

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
    private bool _disposed;

    private VectorReader(MemoryMappedFile mmf, MemoryMappedViewAccessor accessor, int vectorCount, int dimension, long dataStart)
    {
        _mmf = mmf;
        _accessor = accessor;
        _vectorCount = vectorCount;
        _dimension = dimension;
        _dataStart = dataStart;
    }

    public static VectorReader Open(string filePath)
    {
        var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        long offset = 0;

        // Validate header: magic (4 bytes) + version (1 byte)
        int magic = accessor.ReadInt32(offset);
        offset += 4;
        if (magic != CodecConstants.Magic)
            throw new InvalidDataException(
                $"Invalid vector file: expected magic 0x{CodecConstants.Magic:X8}, got 0x{magic:X8}. " +
                "The file may be corrupted or from an incompatible version.");

        byte version = accessor.ReadByte(offset);
        offset += 1;
        if (version > CodecConstants.VectorVersion)
            throw new InvalidDataException(
                $"Unsupported vector format version {version}. " +
                $"This build supports up to version {CodecConstants.VectorVersion}. " +
                "Please upgrade LeanLucene.");

        int vectorCount = accessor.ReadInt32(offset);
        offset += 4;
        int dimension = accessor.ReadInt32(offset);
        offset += 4;
        long dataStart = offset;

        return new VectorReader(mmf, accessor, vectorCount, dimension, dataStart);
    }

    public float[] ReadVector(int docId)
    {
        long offset = _dataStart + (long)docId * _dimension * sizeof(float);
        var vector = new float[_dimension];
        _accessor.ReadArray(offset, vector, 0, _dimension);
        return vector;
    }

    /// <summary>Vector dimension (number of floats per document).</summary>
    public int Dimension => _dimension;

    /// <summary>Total number of vectors stored.</summary>
    public int VectorCount => _vectorCount;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _accessor.Dispose();
        _mmf.Dispose();
    }
}

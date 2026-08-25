using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Reads dense float vectors written by <see cref="VectorWriter"/>.
/// Retains a Store-owned input for zero-copy vector access.
/// </summary>
internal sealed class VectorReader : IDisposable
{
    private readonly IndexInput _input;
    private readonly int _vectorCount;
    private readonly int _dimension;
    private readonly long _dataStart;
    private readonly bool _int8;
    private readonly float _int8Min;
    private readonly float _int8Alpha;
    private readonly IDisposable _frame;
    private bool _disposed;

    private VectorReader(
        IndexInput input,
        int vectorCount, int dimension, long dataStart,
        IDisposable frame,
        bool int8 = false, float int8Min = 0f, float int8Alpha = 0f)
    {
        _input = input;
        _vectorCount = vectorCount;
        _dimension = dimension;
        _dataStart = dataStart;
        _frame = frame;
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
        CodecBodyReadSession? frame = null;
        try
        {
            frame = CodecFileReader.OpenSupported(input, VectorCodecFiles.Float32);

            int vectorCount = input.ReadInt32();
            int dimension = input.ReadInt32();
            if (vectorCount < 0 || dimension < 0)
                throw new InvalidDataException("Vector file contains a negative vector count or dimension.");

            byte format = input.ReadByte();
            if (format is not (byte)VectorQuantisation.None and not (byte)VectorQuantisation.Int8)
                throw new InvalidDataException($"Unsupported vector data format {format}.");

            float int8Min = 0f, int8Alpha = 0f;
            bool isInt8 = format == (byte)VectorQuantisation.Int8;
            if (isInt8)
            {
                int8Min = input.ReadSingle();
                int8Alpha = input.ReadSingle();
            }

            long dataStart = input.Position;
            long stride = isInt8 ? dimension : checked((long)dimension * sizeof(float));
            long dataEnd = checked(dataStart + vectorCount * stride);
            if (dataEnd != checked(frame.BodyStart + frame.BodyLength))
                throw new InvalidDataException("Vector file body length does not match its declared vector count and dimension.");

            return new VectorReader(input, vectorCount, dimension, dataStart, frame, isInt8, int8Min, int8Alpha);
        }
        catch
        {
            frame?.Dispose();
            input.Dispose();
            throw;
        }
    }

    public float[] ReadVector(int docId)
    {
        if ((uint)docId >= (uint)_vectorCount)
            throw new ArgumentOutOfRangeException(nameof(docId));

        var vector = new float[_dimension];
        if (_int8)
        {
            long position = _dataStart + (long)docId * _dimension;
            var packed = _input.BorrowSpan(_dimension, ref position);
            for (int j = 0; j < _dimension; j++)
                vector[j] = _int8Min + _int8Alpha * packed[j];
        }
        else
        {
            long position = _dataStart + (long)docId * _dimension * sizeof(float);
            _input.ReadSingleArray(vector, _dimension, ref position);
        }
        return vector;
    }

    public int Dimension => _dimension;
    public int VectorCount => _vectorCount;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _frame.Dispose();
        _input.Dispose();
    }
}

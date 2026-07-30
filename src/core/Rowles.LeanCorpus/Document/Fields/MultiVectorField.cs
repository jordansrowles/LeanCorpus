using System.Buffers.Binary;

namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>
/// A document-owned collection of equal-dimensional token vectors for exact
/// late-interaction retrieval. An empty collection is persisted distinctly from
/// an absent field.
/// </summary>
public sealed class MultiVectorField : IField
{
    private readonly float[][] _vectors;

    /// <summary>Initialises a multi-vector field from token vectors.</summary>
    public MultiVectorField(string name, IEnumerable<ReadOnlyMemory<float>> vectors, float boost = 1f)
    {
        Name = FieldNameValidator.Validate(name, nameof(name));
        ArgumentNullException.ThrowIfNull(vectors);
        _vectors = vectors.Select(vector => vector.ToArray()).ToArray();
        int dimension = _vectors.Length == 0 ? 0 : _vectors[0].Length;
        if (_vectors.Length > 0 && dimension == 0)
            throw new ArgumentException("Token vectors must contain at least one dimension.", nameof(vectors));
        foreach (float[] vector in _vectors)
        {
            if (vector.Length != dimension)
                throw new ArgumentException("All token vectors in a field must have the same dimension.", nameof(vectors));
            foreach (float value in vector)
            {
                if (!float.IsFinite(value))
                    throw new ArgumentException("Token vectors must contain only finite values.", nameof(vectors));
            }
        }
        Boost = FieldBoostValidator.Validate(boost, nameof(boost));
    }

    /// <inheritdoc/>
    public string Name { get; }
    /// <summary>Immutable document token vectors.</summary>
    public IReadOnlyList<float[]> Vectors => _vectors;
    /// <inheritdoc/>
    public FieldType FieldType => FieldType.MultiVector;
    /// <inheritdoc/>
    public bool IsStored => true;
    /// <inheritdoc/>
    public bool IsIndexed => false;
    /// <inheritdoc/>
    public float Boost { get; }
    /// <inheritdoc/>
    public bool StoreDocValues => true;
    /// <inheritdoc/>
    public FieldIndexOptions IndexOptions => FieldIndexOptions.DocsOnly;

    internal byte[] Encode() => MultiVectorPayload.Encode(_vectors);
}

/// <summary>Versioned binary storage and exact weighted-MaxSim scorer for multi-vectors.</summary>
internal static class MultiVectorPayload
{
    private const uint Magic = 0x3156_4D4C; // LMV1, little endian
    private const byte Version = 1;
    private const int HeaderLength = sizeof(uint) + sizeof(byte) + sizeof(int) + sizeof(int);

    internal static byte[] Encode(IReadOnlyList<float[]> vectors)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        int dimension = vectors.Count == 0 ? 0 : vectors[0].Length;
        long byteLength = checked((long)HeaderLength + (long)vectors.Count * dimension * sizeof(float));
        if (byteLength > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(vectors), "Multi-vector payload exceeds the supported size.");

        var payload = new byte[(int)byteLength];
        var destination = payload.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
        destination[sizeof(uint)] = Version;
        BinaryPrimitives.WriteInt32LittleEndian(destination[(sizeof(uint) + sizeof(byte))..], vectors.Count);
        BinaryPrimitives.WriteInt32LittleEndian(destination[(sizeof(uint) + sizeof(byte) + sizeof(int))..], dimension);
        int offset = HeaderLength;
        foreach (float[] vector in vectors)
        {
            if (vector.Length != dimension)
                throw new InvalidOperationException("Multi-vector payload contains inconsistent dimensions.");
            foreach (float value in vector)
            {
                BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], value);
                offset += sizeof(float);
            }
        }
        return payload;
    }

    internal static float Score(ReadOnlySpan<byte> payload, IReadOnlyList<float[]> queryVectors, IReadOnlyList<float> weights)
    {
        Parse(payload, out int count, out int dimension, out int valuesOffset);
        if (count == 0 || queryVectors.Count == 0)
            return 0f;
        float score = 0f;
        for (int queryIndex = 0; queryIndex < queryVectors.Count; queryIndex++)
        {
            ReadOnlySpan<float> query = queryVectors[queryIndex];
            if (query.Length != dimension)
                throw new ArgumentException("Query token-vector dimension does not match the indexed field.", nameof(queryVectors));
            float maximum = float.NegativeInfinity;
            for (int documentIndex = 0; documentIndex < count; documentIndex++)
            {
                int offset = valuesOffset + documentIndex * dimension * sizeof(float);
                float dot = 0f;
                for (int dimensionIndex = 0; dimensionIndex < dimension; dimensionIndex++)
                    dot += query[dimensionIndex] * BinaryPrimitives.ReadSingleLittleEndian(payload[(offset + dimensionIndex * sizeof(float))..]);
                maximum = MathF.Max(maximum, dot);
            }
            score += weights[queryIndex] * maximum;
        }
        return score;
    }

    private static void Parse(ReadOnlySpan<byte> payload, out int count, out int dimension, out int valuesOffset)
    {
        if (payload.Length < HeaderLength ||
            BinaryPrimitives.ReadUInt32LittleEndian(payload) != Magic ||
            payload[sizeof(uint)] != Version)
        {
            throw new InvalidDataException("Invalid late-interaction multi-vector payload.");
        }
        count = BinaryPrimitives.ReadInt32LittleEndian(payload[(sizeof(uint) + sizeof(byte))..]);
        dimension = BinaryPrimitives.ReadInt32LittleEndian(payload[(sizeof(uint) + sizeof(byte) + sizeof(int))..]);
        if (count < 0 || dimension < 0 || count > 0 && dimension == 0)
            throw new InvalidDataException("Invalid late-interaction multi-vector dimensions.");
        long expectedLength = checked((long)HeaderLength + (long)count * dimension * sizeof(float));
        if (expectedLength != payload.Length)
            throw new InvalidDataException("Truncated or trailing late-interaction multi-vector payload.");
        valuesOffset = HeaderLength;
    }
}

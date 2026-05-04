namespace Rowles.LeanLucene.Codecs.Vectors;

/// <summary>
/// Writes dense float vectors with a fixed-dimension layout for implicit offset indexing.
/// Format: [int: vectorCount][int: dimension][float[][]: vector data].
/// </summary>
internal static class VectorWriter
{
    internal static void Write(string filePath, ReadOnlyMemory<float>[] vectors)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.WriteHeader(writer, CodecConstants.VectorVersion);

        int dimension = 0;
        for (int i = 0; i < vectors.Length; i++)
        {
            if (vectors[i].Length > 0) { dimension = vectors[i].Length; break; }
        }

        writer.Write(vectors.Length);
        writer.Write(dimension);

        Span<float> zero = dimension <= 256 ? stackalloc float[dimension] : new float[dimension];
        zero.Clear();

        for (int i = 0; i < vectors.Length; i++)
        {
            var span = vectors[i].Length == dimension ? vectors[i].Span : zero;
            for (int j = 0; j < dimension; j++)
                writer.Write(span[j]);
        }
    }

    /// <summary>
    /// Writes a per-field dense vector file. Missing docs are zero-padded so reader offset arithmetic
    /// remains valid; HNSW search never visits zero-padded docs because they are absent from the graph.
    /// </summary>
    internal static void WriteField(
        string filePath,
        int docCount,
        int dimension,
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> vectorsByDoc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(docCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.WriteHeader(writer, CodecConstants.VectorVersion);
        writer.Write(docCount);
        writer.Write(dimension);

        Span<float> zero = dimension <= 256 ? stackalloc float[dimension] : new float[dimension];
        zero.Clear();

        for (int i = 0; i < docCount; i++)
        {
            ReadOnlySpan<float> span = zero;
            if (vectorsByDoc.TryGetValue(i, out var v))
            {
                if (v.Length != dimension)
                    throw new InvalidDataException($"Vector for document {i} has dimension {v.Length}; expected {dimension}.");
                span = v.Span;
            }
            for (int j = 0; j < dimension; j++)
                writer.Write(span[j]);
        }
    }
}

using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>
/// kNN query over an unsigned-byte vector field. The bytes are converted exactly
/// to Float32 at the public API boundary and then use the ordinary vector path.
/// </summary>
public sealed class ByteVectorQuery : VectorQuery
{
    /// <summary>Initialises a byte-vector query.</summary>
    public ByteVectorQuery(
        string field,
        ReadOnlySpan<byte> queryVector,
        int topK = 10,
        int efSearch = 0,
        int oversamplingFactor = 1,
        Query? filter = null,
        int maxVisitedNodes = 0)
        : base(field, Convert(queryVector), topK, efSearch, oversamplingFactor, filter, maxVisitedNodes)
    {
        QueryBytes = queryVector.ToArray();
    }

    /// <summary>Gets the original unsigned-byte query values.</summary>
    public byte[] QueryBytes { get; }

    private static float[] Convert(ReadOnlySpan<byte> values)
    {
        if (values.IsEmpty)
            throw new ArgumentException("Byte query vectors must contain at least one dimension.", nameof(values));
        var converted = new float[values.Length];
        for (int i = 0; i < converted.Length; i++)
            converted[i] = values[i];
        return converted;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is ByteVectorQuery other &&
        base.Equals(other) &&
        QueryBytes.AsSpan().SequenceEqual(other.QueryBytes);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        foreach (byte value in QueryBytes)
            hash.Add(value);
        return hash.ToHashCode();
    }
}

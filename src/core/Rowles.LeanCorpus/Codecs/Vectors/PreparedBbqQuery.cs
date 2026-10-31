namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>A reusable centroid-relative four-bit query for v3 BBQ candidate scoring.</summary>
internal sealed class PreparedBbqQuery
{
    private readonly byte[] _codes;
    private readonly byte[] _bitPlanes;

    private PreparedBbqQuery(byte[] codes, byte[] bitPlanes, BbqOsqCorrections corrections)
    {
        _codes = codes;
        _bitPlanes = bitPlanes;
        Corrections = corrections;
    }

    internal int Dimension => _codes.Length;

    internal BbqOsqCorrections Corrections { get; }

    internal ReadOnlySpan<byte> QueryCodes => _codes;

    internal ReadOnlySpan<byte> BitPlanes => _bitPlanes;

    /// <summary>Quantises and transposes the query once for reuse across a candidate set.</summary>
    internal static PreparedBbqQuery Prepare(ReadOnlySpan<float> query, ReadOnlySpan<float> centroid)
    {
        ArgumentOutOfRangeException.ThrowIfZero(query.Length);
        if (centroid.Length != query.Length)
            throw new ArgumentException("Centroid length must match the query dimension.", nameof(centroid));

        int dimension = query.Length;
        int packedLength = checked((dimension + 7) / 8);
        byte[] codes = new byte[dimension];
        byte[] bitPlanes = new byte[checked(packedLength * 4)];
        float[] residuals = System.Buffers.ArrayPool<float>.Shared.Rent(dimension);
        try
        {
            BbqOsqCorrections corrections = OptimizedScalarQuantiser.Quantize(
                query, centroid, bits: 4, residuals.AsSpan(0, dimension), codes);
            for (int index = 0; index < dimension; index++)
            {
                int bit = 1 << (7 - (index & 7));
                int packedIndex = index >> 3;
                byte code = codes[index];
                for (int plane = 0; plane < 4; plane++)
                {
                    if ((code & (1 << plane)) != 0)
                        bitPlanes[plane * packedLength + packedIndex] |= (byte)bit;
                }
            }

            return new PreparedBbqQuery(codes, bitPlanes, corrections);
        }
        finally
        {
            System.Buffers.ArrayPool<float>.Shared.Return(residuals, clearArray: true);
        }
    }
}

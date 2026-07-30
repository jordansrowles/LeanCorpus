using System.Buffers;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>Prepared asymmetric product-quantisation distance lookup table.</summary>
internal sealed class ProductQuantisationQuery : IDisposable
{
    private readonly QuantisedVectorReader _reader;
    private readonly float[]? _lookup;
    private readonly float[]? _query;
    private readonly int _centroidCount;
    private readonly bool _dotProduct;
    private readonly bool _pooledLookup;

    internal ProductQuantisationQuery(
        QuantisedVectorReader reader,
        float[] lookup,
        int centroidCount,
        bool dotProduct,
        bool pooledLookup)
    {
        _reader = reader;
        _lookup = lookup;
        _centroidCount = centroidCount;
        _dotProduct = dotProduct;
        _pooledLookup = pooledLookup;
    }

    internal ProductQuantisationQuery(
        QuantisedVectorReader reader,
        ReadOnlySpan<float> query,
        bool dotProduct)
    {
        _reader = reader;
        _query = query.ToArray();
        _dotProduct = dotProduct;
    }

    /// <summary>Returns HNSW distance, where lower values are better.</summary>
    internal float DistanceTo(int docId)
    {
        if (_lookup is null)
            return _reader.ProductQueryDistance(_query!, docId, _dotProduct);

        ReadOnlySpan<byte> codes = _reader.GetRawProductRoutingCodes(docId);
        float value = 0f;
        for (int sub = 0; sub < codes.Length; sub++)
            value += _lookup[sub * _centroidCount + codes[sub]];
        return _dotProduct ? -value : value;
    }

    public void Dispose()
    {
        if (_pooledLookup)
            ArrayPool<float>.Shared.Return(_lookup!, clearArray: false);
    }
}

namespace Rowles.LeanLucene.Util;

/// <summary>
/// <see cref="IBitSet"/> adapter over a <see cref="RoaringBitmap"/>.
/// </summary>
internal sealed class RoaringBitmapBitSet : IBitSet
{
    private readonly RoaringBitmap _bitmap;

    public RoaringBitmapBitSet(RoaringBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        _bitmap = bitmap;
    }

    public bool Contains(int docId) => _bitmap.Contains(docId);

    public int Cardinality => _bitmap.Cardinality;
}

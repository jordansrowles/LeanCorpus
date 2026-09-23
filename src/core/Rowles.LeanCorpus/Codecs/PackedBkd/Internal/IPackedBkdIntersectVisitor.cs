namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

/// <summary>Receives packed BKD hits without allocating a point object for each value.</summary>
internal interface IPackedBkdIntersectVisitor
{
    PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum);
    void Visit(int docId);
    void Visit(int docId, ReadOnlySpan<byte> packedValue);
}

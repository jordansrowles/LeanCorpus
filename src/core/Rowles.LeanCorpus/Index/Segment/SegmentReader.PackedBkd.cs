
namespace Rowles.LeanCorpus.Index.Segment;

public sealed partial class SegmentReader
{
    internal IReadOnlyList<string> GetPackedBkdFieldNames()
    {
        using var lease = AcquireReadLease();
        return lease.State.GetPackedBkdFieldNames();
    }

    internal bool TryGetPackedBkdFieldMetadata(string field, out PackedBkdFieldMetadata metadata)
    {
        using var lease = AcquireReadLease();
        return lease.State.TryGetPackedBkdFieldMetadata(field, out metadata);
    }

    internal bool IntersectPackedBkd<TVisitor>(string field, ref TVisitor visitor)
        where TVisitor : struct, IPackedBkdIntersectVisitor
    {
        using var lease = AcquireReadLease();
        return lease.State.IntersectPackedBkd(field, ref visitor);
    }

    internal void DeepValidatePackedBkd()
    {
        using var lease = AcquireReadLease();
        lease.State.DeepValidatePackedBkd();
    }

    internal void ValidatePackedBkdChecksum()
    {
        using var lease = AcquireReadLease();
        lease.State.ValidatePackedBkdChecksum();
    }
}

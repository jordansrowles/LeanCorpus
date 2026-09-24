using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Index.Segment;

public sealed partial class SegmentReader
{
    internal IReadOnlyList<string> GetShapeDocValuesFieldNames()
    {
        using var lease = AcquireReadLease();
        return lease.State.GetShapeDocValuesFieldNames();
    }

    internal bool TryGetShapeDocValuesFieldMetadata(string field, out ShapeDocValuesFieldMetadata metadata)
    {
        using var lease = AcquireReadLease();
        return lease.State.TryGetShapeDocValuesFieldMetadata(field, out metadata);
    }

    internal bool TryGetShapeDocValuesRecordMetadata(
        string field,
        int documentId,
        out ShapeDocValuesRecordMetadata metadata)
    {
        using var lease = AcquireReadLease();
        return lease.State.TryGetShapeDocValuesRecordMetadata(field, documentId, out metadata);
    }

    internal int VisitShapeDocValuesPrimitives(string field, int documentId, Action<ShapePrimitive> visitor)
    {
        using var lease = AcquireReadLease();
        return lease.State.VisitShapeDocValuesPrimitives(field, documentId, visitor);
    }

    internal byte[] ReadShapeDocValuesRecordBytes(string field, int documentId)
    {
        using var lease = AcquireReadLease();
        return lease.State.ReadShapeDocValuesRecordBytes(field, documentId);
    }

    internal void ValidateShapeDocValuesChecksum()
    {
        using var lease = AcquireReadLease();
        lease.State.ValidateShapeDocValuesChecksum();
    }

    internal void DeepValidateShapeDocValues()
    {
        using var lease = AcquireReadLease();
        lease.State.DeepValidateShapeDocValues();
    }
}

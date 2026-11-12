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
        if (TryGetFastState(out var state))
            return state.TryGetShapeDocValuesRecordMetadata(field, documentId, out metadata);
        using var lease = AcquireReadLease();
        return lease.State.TryGetShapeDocValuesRecordMetadata(field, documentId, out metadata);
    }

    internal int VisitShapeDocValuesPrimitives(string field, int documentId, Action<ShapePrimitive> visitor)
    {
        if (TryGetFastState(out var state))
            return state.VisitShapeDocValuesPrimitives(field, documentId, visitor);
        using var lease = AcquireReadLease();
        return lease.State.VisitShapeDocValuesPrimitives(field, documentId, visitor);
    }

    /// <summary>Visits a document's primitives using record metadata already read for it.</summary>
    internal int VisitShapeDocValuesPrimitives(
        string field,
        in ShapeDocValuesRecordMetadata record,
        Action<ShapePrimitive> visitor)
    {
        if (TryGetFastState(out var state))
            return state.VisitShapeDocValuesPrimitives(field, record, visitor);
        using var lease = AcquireReadLease();
        return lease.State.VisitShapeDocValuesPrimitives(field, record, visitor);
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

    internal void ValidateShapeDocValuesRecord(string field, int documentId)
    {
        using var lease = AcquireReadLease();
        lease.State.ValidateShapeDocValuesRecord(field, documentId);
    }

    internal void DeepValidateShapeDocValues()
    {
        using var lease = AcquireReadLease();
        lease.State.DeepValidateShapeDocValues();
    }
}

using System.Collections.ObjectModel;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Immutable runtime metadata captured by a segment reader. Persisted metadata remains
/// represented by the mutable <see cref="SegmentInfo"/> DTO.
/// </summary>
public sealed class SegmentDescriptor
{
    internal SegmentDescriptor(SegmentInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        info.Validate();
        SegmentInfo snapshot = info.DeepCopy();

        SegmentId = snapshot.SegmentId;
        DocCount = snapshot.DocCount;
        LiveDocCount = snapshot.LiveDocCount;
        TotalBytes = snapshot.TotalBytes;
        CodecBytes = new ReadOnlyDictionary<string, long>(snapshot.CodecBytes);
        CommitGeneration = snapshot.CommitGeneration;
        IsCompoundFile = snapshot.IsCompoundFile;
        FieldNames = snapshot.FieldNames.AsReadOnly();
        IndexSortFields = snapshot.IndexSortFields?.AsReadOnly();
        VectorFields = snapshot.VectorFields.AsReadOnly();
        SpatialFields = snapshot.SpatialFields.AsReadOnly();
        DelGeneration = snapshot.DelGeneration;
        MinSequenceNumber = snapshot.MinSequenceNumber;
        MaxSequenceNumber = snapshot.MaxSequenceNumber;
        EarliestSoftDeleteTimestamp = snapshot.EarliestSoftDeleteTimestamp;
    }

    /// <summary>Gets the unique segment identifier.</summary>
    public string SegmentId { get; }

    /// <summary>Gets the physical document count, including deleted documents.</summary>
    public int DocCount { get; }

    /// <summary>Gets the live document count captured for this reader.</summary>
    public int LiveDocCount { get; }

    /// <summary>Gets the total byte size captured for this reader.</summary>
    public long TotalBytes { get; }

    /// <summary>Gets immutable byte counts grouped by file extension.</summary>
    public IReadOnlyDictionary<string, long> CodecBytes { get; }

    /// <summary>Gets the fraction of documents currently deleted from this segment.</summary>
    public double DeletionDensity => DocCount == 0 ? 0 : 1.0 - (double)LiveDocCount / DocCount;

    /// <summary>Gets the commit generation at which this segment was created.</summary>
    public int CommitGeneration { get; }

    /// <summary>Gets whether immutable codec data is held in a compound file.</summary>
    public bool IsCompoundFile { get; }

    /// <summary>Gets the immutable names of indexed fields in this segment.</summary>
    public IReadOnlyList<string> FieldNames { get; }

    /// <summary>Gets the immutable index-sort key, or null when the segment is unsorted.</summary>
    public IReadOnlyList<string>? IndexSortFields { get; }

    /// <summary>Gets immutable vector field metadata.</summary>
    public IReadOnlyList<VectorFieldInfo> VectorFields { get; }

    /// <summary>Gets immutable spatial field metadata.</summary>
    public IReadOnlyList<SpatialFieldInfo> SpatialFields { get; }

    /// <summary>Gets the deletion generation selected by this reader, or null for legacy state.</summary>
    public int? DelGeneration { get; }

    /// <summary>Gets the inclusive lower sequence-number bound, when sequence tracking is enabled.</summary>
    public long? MinSequenceNumber { get; }

    /// <summary>Gets the inclusive upper sequence-number bound, when sequence tracking is enabled.</summary>
    public long? MaxSequenceNumber { get; }

    /// <summary>Gets the earliest soft-delete timestamp captured for this segment.</summary>
    public long? EarliestSoftDeleteTimestamp { get; }
}

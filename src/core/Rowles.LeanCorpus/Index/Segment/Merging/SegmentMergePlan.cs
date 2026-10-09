using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Index.Segment.Merging;

/// <summary>Completed immutable merge decisions, borrowing readers from SegmentMerger.</summary>
internal sealed class SegmentMergePlan(
    List<SegmentInfo> segments,
    IReadOnlyDictionary<string, SegmentReader> readers,
    string segmentId,
    string basePath,
    int commitGeneration,
    VectorQuantisation vectorQuantisation,
    SegmentMergeDocumentMap documentMap,
    List<(int DocId, long Timestamp)> softDeletes,
    HashSet<string> fieldNames,
    string[]? indexSortFields,
    List<SpatialFieldInfo> spatialFields,
    Dictionary<string, VectorFieldContract> vectorContracts,
    long? minimumSequenceNumber,
    long? maximumSequenceNumber)
{
    internal IReadOnlyList<SegmentInfo> Segments { get; } = segments.AsReadOnly();
    internal IReadOnlyDictionary<string, SegmentReader> Readers { get; } = readers;
    internal string SegmentId { get; } = segmentId;
    internal string BasePath { get; } = basePath;
    internal int CommitGeneration { get; } = commitGeneration;
    internal VectorQuantisation VectorQuantisation { get; } = vectorQuantisation;
    internal SegmentMergeDocumentMap DocumentMap { get; } = documentMap;
    internal int TotalDocs => DocumentMap.Count;
    internal IReadOnlyList<(int DocId, long Timestamp)> SoftDeletes { get; } = softDeletes.AsReadOnly();
    internal IReadOnlyCollection<string> FieldNames { get; } = fieldNames;
    internal IReadOnlyList<string>? IndexSortFields { get; } = indexSortFields is null ? null : Array.AsReadOnly(indexSortFields);
    internal IReadOnlyList<SpatialFieldInfo> SpatialFields { get; } = spatialFields.AsReadOnly();
    internal IReadOnlyDictionary<string, VectorFieldContract> VectorContracts { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, VectorFieldContract>(vectorContracts);
    internal long? MinimumSequenceNumber { get; } = minimumSequenceNumber;
    internal long? MaximumSequenceNumber { get; } = maximumSequenceNumber;
}

internal readonly record struct VectorFieldContract(int Dimension, bool Normalised);

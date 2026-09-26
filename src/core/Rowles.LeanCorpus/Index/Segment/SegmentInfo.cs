using System.Text.Json;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Immutable codec metadata and the writer's current view of mutable segment state.
/// </summary>
public sealed class SegmentInfo
{
    /// <summary>Gets the unique identifier for this segment (e.g. "seg_0").</summary>
    public string SegmentId { get; init; } = string.Empty;

    /// <summary>Gets the total number of documents in this segment, including deleted documents.</summary>
    public int DocCount { get; init; }

    /// <summary>Gets the number of live (non-deleted) documents in this segment.</summary>
    public int LiveDocCount { get; set; }

    /// <summary>Gets the total bytes occupied by this segment's files.</summary>
    public long TotalBytes { get; set; }

    /// <summary>Gets segment bytes grouped by file extension.</summary>
    public Dictionary<string, long> CodecBytes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets the fraction of documents currently deleted from this segment.</summary>
    public double DeletionDensity => DocCount == 0 ? 0 : 1.0 - (double)LiveDocCount / DocCount;

    /// <summary>Gets the commit generation at which this segment was created.</summary>
    public int CommitGeneration { get; init; }

    /// <summary>
    /// Gets a value indicating whether immutable codec files are stored in the segment's
    /// memory-mapped compound file. The segment metadata file, deletion files, and segment
    /// statistics remain separate.
    /// </summary>
    public bool IsCompoundFile { get; set; }

    /// <summary>Gets the names of all indexed fields present in this segment.</summary>
    public List<string> FieldNames { get; init; } = [];

    /// <summary>
    /// Serialised index sort fields for this segment. Null if the segment is unsorted.
    /// Each entry is "Type:FieldName:Descending" (e.g. "Numeric:price:True").
    /// </summary>
    public List<string>? IndexSortFields { get; init; }

    /// <summary>Per-field vector metadata for vectors stored in this segment.</summary>
    public List<VectorFieldInfo> VectorFields { get; init; } = [];

    /// <summary>Per-field coordinate and encoding metadata for spatial values.</summary>
    public List<SpatialFieldInfo> SpatialFields
    {
        get => _spatialFields ??= [];
        init
        {
            _spatialFields = value;
        }
    }

    private List<SpatialFieldInfo>? _spatialFields = [];

    /// <summary>
    /// The commit generation at which the current live-document file was written.
    /// When set, the file is named <c>{SegmentId}_gen_{DelGeneration}.del</c>.
    /// When null, the legacy <c>{SegmentId}.del</c> path is used for backward compatibility.
    /// </summary>
    public int? DelGeneration { get; set; }

    /// <summary>
    /// Inclusive lower bound of the sequence numbers assigned to documents in this segment.
    /// Only set when sequence number tracking is enabled.
    /// </summary>
    public long? MinSequenceNumber { get; set; }

    /// <summary>
    /// Inclusive upper bound of the sequence numbers assigned to documents in this segment.
    /// Only set when sequence number tracking is enabled.
    /// </summary>
    public long? MaxSequenceNumber { get; set; }

    /// <summary>Gets the smallest soft-delete timestamp (Unix milliseconds) among live soft-deleted docs, or null if none exist.</summary>
    public long? EarliestSoftDeleteTimestamp { get; set; }

    /// <summary>Writes this segment metadata to a JSON file at the specified path.</summary>
    /// <param name="filePath">The path of the <c>.seg</c> file to write.</param>
    public void WriteTo(string filePath)
    {
        var json = JsonSerializer.Serialize(this, LeanCorpusJsonContext.Default.SegmentInfo);
        IndexAtomicFileWriter.WriteText(filePath, json, durable: false);
    }

    /// <summary>Reads and deserialises segment metadata from the specified JSON file.</summary>
    /// <param name="filePath">The path of the <c>.seg</c> file to read.</param>
    /// <returns>The deserialised <see cref="SegmentInfo"/>.</returns>
    /// <exception cref="InvalidDataException">Thrown if the file cannot be deserialised or fails validation.</exception>
    public static SegmentInfo ReadFrom(string filePath)
    {
        var json = FileOpenRetry.ReadAllText(filePath);
        var info = JsonSerializer.Deserialize(json, LeanCorpusJsonContext.Default.SegmentInfo)
            ?? throw new InvalidDataException("Failed to deserialise segment info.");
        info.Validate();
        return info;
    }

    /// <summary>Creates a deep mutable copy, including all nested segment metadata.</summary>
    internal SegmentInfo DeepCopy() => new()
    {
        SegmentId = SegmentId,
        DocCount = DocCount,
        LiveDocCount = LiveDocCount,
        TotalBytes = TotalBytes,
        CodecBytes = new Dictionary<string, long>(CodecBytes, StringComparer.Ordinal),
        CommitGeneration = CommitGeneration,
        IsCompoundFile = IsCompoundFile,
        FieldNames = [.. FieldNames],
        IndexSortFields = IndexSortFields is null ? null : [.. IndexSortFields],
        VectorFields = VectorFields.Select(static field => new VectorFieldInfo
        {
            FieldName = field.FieldName,
            Dimension = field.Dimension,
            Normalised = field.Normalised,
            HasHnsw = field.HasHnsw,
            Quantisation = field.Quantisation
        }).ToList(),
        SpatialFields = SpatialFields.Select(static field => new SpatialFieldInfo
        {
            FieldName = field.FieldName,
            Kind = field.Kind
        }).ToList(),
        DelGeneration = DelGeneration,
        MinSequenceNumber = MinSequenceNumber,
        MaxSequenceNumber = MaxSequenceNumber,
        EarliestSoftDeleteTimestamp = EarliestSoftDeleteTimestamp
    };

    /// <summary>
    /// Validates invariants after deserialisation. Throws <see cref="InvalidDataException"/>
    /// when required fields are missing, empty, or out of range.
    /// </summary>
    internal void Validate()
    {
        if (string.IsNullOrEmpty(SegmentId))
            throw new InvalidDataException("Segment metadata has a null or empty SegmentId.");
        if (FieldNames is null)
            throw new InvalidDataException($"Segment '{SegmentId}' has a null FieldNames list.");
        if (VectorFields is null)
            throw new InvalidDataException($"Segment '{SegmentId}' has a null VectorFields list.");
        if (CodecBytes is null)
            throw new InvalidDataException($"Segment '{SegmentId}' has null codec byte metadata.");
        foreach (var vf in VectorFields)
            vf.Validate();

        var spatialFieldNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spatialField in SpatialFields)
        {
            if (spatialField is null)
                throw new InvalidDataException($"Segment '{SegmentId}' contains null spatial field metadata.");
            spatialField.Validate();
            if (!spatialFieldNames.Add(spatialField.FieldName))
                throw new InvalidDataException($"Segment '{SegmentId}' contains duplicate spatial metadata for field '{spatialField.FieldName}'.");
        }
    }
}

using System.Text.Json;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Immutable codec metadata and the writer's current view of mutable segment state.
/// </summary>
public sealed class SegmentInfo
{
    internal enum ValidationIssue
    {
        InvalidDocCount,
        InvalidLiveDocCount
    }

    internal const string ValidationIssueDataKey = "Rowles.LeanCorpus.SegmentInfo.ValidationIssue";

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
        string json = FileOpenRetry.ReadAllText(filePath);
        SegmentInfo info;
        try
        {
            info = JsonSerializer.Deserialize(json, LeanCorpusJsonContext.Default.SegmentInfo)
                ?? throw new InvalidDataException("Failed to deserialise segment info.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Failed to deserialise segment info from '{filePath}'.", ex);
        }

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
        // ReadFrom and SegmentDescriptor share this invariant boundary so persisted and
        // in-memory metadata cannot acquire different validation rules.
        if (string.IsNullOrEmpty(SegmentId))
            throw new InvalidDataException("Segment metadata has a null or empty SegmentId.");

        if (DocCount < 0)
            throw CreateValidationException(
                $"Segment '{SegmentId}' has invalid DocCount={DocCount}.",
                ValidationIssue.InvalidDocCount);
        if (LiveDocCount < 0 || LiveDocCount > DocCount)
            throw CreateValidationException(
                $"Segment '{SegmentId}' has LiveDocCount={LiveDocCount}, outside [0,{DocCount}].",
                ValidationIssue.InvalidLiveDocCount);
        if (TotalBytes < 0)
            throw new InvalidDataException($"Segment '{SegmentId}' has negative TotalBytes={TotalBytes}.");
        if (CommitGeneration < 0)
            throw new InvalidDataException($"Segment '{SegmentId}' has negative CommitGeneration={CommitGeneration}.");
        if (DelGeneration is < 0)
            throw new InvalidDataException($"Segment '{SegmentId}' has negative DelGeneration={DelGeneration}.");
        if (EarliestSoftDeleteTimestamp is < 0)
            throw new InvalidDataException($"Segment '{SegmentId}' has a negative earliest soft-delete timestamp.");
        if (MinSequenceNumber.HasValue != MaxSequenceNumber.HasValue)
            throw new InvalidDataException($"Segment '{SegmentId}' has an incomplete sequence-number range.");
        if (MinSequenceNumber is long minSequenceNumber && MaxSequenceNumber is long maxSequenceNumber)
        {
            if (minSequenceNumber < 0 || maxSequenceNumber < 0)
                throw new InvalidDataException($"Segment '{SegmentId}' has a negative sequence-number range [{minSequenceNumber},{maxSequenceNumber}].");
            if (minSequenceNumber > maxSequenceNumber)
                throw new InvalidDataException($"Segment '{SegmentId}' has MinSequenceNumber={minSequenceNumber} greater than MaxSequenceNumber={maxSequenceNumber}.");
        }

        if (FieldNames is null)
            throw new InvalidDataException($"Segment '{SegmentId}' has a null FieldNames list.");
        if (VectorFields is null)
            throw new InvalidDataException($"Segment '{SegmentId}' has a null VectorFields list.");
        if (CodecBytes is null)
            throw new InvalidDataException($"Segment '{SegmentId}' has null codec byte metadata.");

        foreach (var (extension, byteCount) in CodecBytes)
        {
            if (string.IsNullOrWhiteSpace(extension))
                throw new InvalidDataException($"Segment '{SegmentId}' has an empty codec byte extension.");
            if (byteCount < 0)
                throw new InvalidDataException($"Segment '{SegmentId}' has negative byte count {byteCount} for extension '{extension}'.");
        }

        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? fieldName in FieldNames)
        {
            ValidateFieldName(fieldName, $"Segment '{SegmentId}' field metadata");
            if (!fieldNames.Add(fieldName!))
                throw new InvalidDataException($"Segment '{SegmentId}' contains duplicate field metadata for '{fieldName}'.");
        }

        if (IndexSortFields is { Count: 0 })
            throw new InvalidDataException($"Segment '{SegmentId}' has an empty IndexSortFields list.");
        if (IndexSortFields is not null)
        {
            for (int i = 0; i < IndexSortFields.Count; i++)
            {
                if (!IndexSort.TryParseSerialisedField(IndexSortFields[i], out _))
                    throw new InvalidDataException($"Segment '{SegmentId}' has invalid index-sort metadata at position {i}.");
            }
        }

        var vectorFieldNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var vf in VectorFields)
        {
            if (vf is null)
                throw new InvalidDataException($"Segment '{SegmentId}' contains null vector field metadata.");
            vf.Validate();
            if (!vectorFieldNames.Add(vf.FieldName))
                throw new InvalidDataException($"Segment '{SegmentId}' contains duplicate vector metadata for field '{vf.FieldName}'.");
        }

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

    private static void ValidateFieldName(string? fieldName, string description)
    {
        try
        {
            FieldNameValidator.Validate(fieldName!, nameof(FieldNames));
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"{description} contains an invalid field name.", ex);
        }
    }

    private static InvalidDataException CreateValidationException(string message, ValidationIssue issue)
    {
        var exception = new InvalidDataException(message);
        exception.Data[ValidationIssueDataKey] = issue;
        return exception;
    }
}

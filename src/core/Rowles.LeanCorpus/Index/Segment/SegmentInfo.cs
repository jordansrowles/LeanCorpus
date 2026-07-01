using System.Text.Json;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Metadata record for a single immutable segment.
/// </summary>
public sealed class SegmentInfo
{
    /// <summary>Gets the unique identifier for this segment (e.g. "seg_0").</summary>
    public string SegmentId { get; init; } = string.Empty;

    /// <summary>Gets the total number of documents in this segment, including deleted documents.</summary>
    public int DocCount { get; init; }

    /// <summary>Gets the number of live (non-deleted) documents in this segment.</summary>
    public int LiveDocCount { get; set; }

    /// <summary>Gets the commit generation at which this segment was created.</summary>
    public int CommitGeneration { get; init; }

    /// <summary>Gets the names of all indexed fields present in this segment.</summary>
    public List<string> FieldNames { get; init; } = [];

    /// <summary>
    /// Serialised index sort fields for this segment. Null if the segment is unsorted.
    /// Each entry is "Type:FieldName:Descending" (e.g. "Numeric:price:True").
    /// </summary>
    public List<string>? IndexSortFields { get; init; }

    /// <summary>Per-field vector metadata for vectors stored in this segment.</summary>
    public List<VectorFieldInfo> VectorFields { get; init; } = [];

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
        foreach (var vf in VectorFields)
            vf.Validate();
    }
}

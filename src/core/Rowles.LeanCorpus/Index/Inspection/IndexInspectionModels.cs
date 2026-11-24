using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Index.Segment;

namespace Rowles.LeanCorpus.Index.Inspection;

/// <summary>Stable summary of the committed index snapshot visible to an inspector.</summary>
/// <param name="DirectoryPath">The inspected index directory.</param>
/// <param name="CommitGeneration">The latest readable commit generation, when present.</param>
/// <param name="ContentToken">The latest commit content token, when present.</param>
/// <param name="DocumentCount">The committed document count, including deleted documents.</param>
/// <param name="LiveDocumentCount">The committed live document count.</param>
/// <param name="SegmentCount">The committed segment count.</param>
/// <param name="FileCount">The number of files directly in the index directory.</param>
/// <param name="TotalSizeBytes">The total size of files directly in the index directory.</param>
/// <param name="HasVectors">Whether one or more vector fields are present.</param>
/// <param name="HasHnsw">Whether one or more vector fields have an HNSW graph.</param>
/// <param name="HasOrdinalDocValues">Whether one or more fields have ordinal DocValues suitable for facet exploration.</param>
/// <param name="HasGeo">Whether one or more spatial fields are present.</param>
/// <param name="CompoundSegmentCount">The number of compound segments.</param>
/// <param name="OrphanFileCount">The number of recognised files not referenced by the commit.</param>
/// <param name="HasUnsupportedFutureFormat">Whether a file uses a future unsupported format.</param>
/// <param name="HasOutdatedCodec">Whether a recognised file is not at the current format version.</param>
/// <param name="HasUnknownFormat">Whether a self-identifying file declares an unknown format.</param>
public sealed record IndexInspectionOverview(
    string DirectoryPath,
    int? CommitGeneration,
    long? ContentToken,
    int DocumentCount,
    int LiveDocumentCount,
    int SegmentCount,
    int FileCount,
    long TotalSizeBytes,
    bool HasVectors,
    bool HasHnsw,
    bool HasOrdinalDocValues,
    bool HasGeo,
    int CompoundSegmentCount,
    int OrphanFileCount,
    bool HasUnsupportedFutureFormat,
    bool HasOutdatedCodec,
    bool HasUnknownFormat)
{
    /// <summary>Gets the number of deleted documents in the committed snapshot.</summary>
    public int DeletedDocumentCount => DocumentCount - LiveDocumentCount;

    /// <summary>Gets whether one or more vector fields have a Float32 vector graph (<c>.vgr</c>).</summary>
    public bool HasVectorGraph { get; init; }
}

/// <summary>Stable summary of one committed segment and its inspected files.</summary>
/// <param name="SegmentId">The segment identifier.</param>
/// <param name="DocumentCount">The physical document count.</param>
/// <param name="LiveDocumentCount">The live document count.</param>
/// <param name="CommitGeneration">The segment creation generation.</param>
/// <param name="DeleteGeneration">The selected deletion generation.</param>
/// <param name="TotalSizeBytes">The total size captured for the segment.</param>
/// <param name="IsCompound">Whether immutable codec files are stored in a compound file.</param>
/// <param name="FieldNames">The indexed field names.</param>
/// <param name="VectorFields">The vector field metadata.</param>
/// <param name="SpatialFields">The spatial field metadata.</param>
/// <param name="Files">The inspected files belonging to the segment.</param>
/// <param name="MissingFiles">The required files missing from the segment.</param>
/// <param name="Warnings">Non-fatal segment inspection warnings.</param>
public sealed record IndexInspectionSegment(
    string SegmentId,
    int? DocumentCount,
    int? LiveDocumentCount,
    int? CommitGeneration,
    int? DeleteGeneration,
    long TotalSizeBytes,
    bool? IsCompound,
    IReadOnlyList<string> FieldNames,
    IReadOnlyList<VectorFieldInfo> VectorFields,
    IReadOnlyList<SpatialFieldInfo> SpatialFields,
    IReadOnlyList<CodecFileInventory> Files,
    IReadOnlyList<string> MissingFiles,
    IReadOnlyList<string> Warnings);

/// <summary>Stable summary of an index field and its discoverable capabilities.</summary>
/// <param name="Name">The field name.</param>
/// <param name="Indexed">Whether indexed terms are present.</param>
/// <param name="Stored">Whether stored values were found.</param>
/// <param name="HasDocValues">Whether DocValues were found.</param>
/// <param name="HasTermVectors">Whether term vectors may be available for the field.</param>
/// <param name="HasOrdinalDocValues">Whether sorted or sorted-set DocValues are present.</param>
/// <param name="SegmentCount">The number of segments containing the field.</param>
/// <param name="DocumentCount">The number of live documents with a value.</param>
/// <param name="UniqueTermCount">The number of unique indexed terms, when indexed.</param>
/// <param name="TotalTermFrequency">The total live term frequency, when indexed.</param>
/// <param name="VectorDimension">The vector dimension, when this is a vector field.</param>
/// <param name="VectorNormalised">Whether vector values are normalised, when known.</param>
/// <param name="HasHnsw">Whether an HNSW graph exists for this field.</param>
/// <param name="HnswNodeCount">The graph node count, when available.</param>
/// <param name="VectorQuantisation">The vector quantisation mode, when this is a vector field.</param>
/// <param name="SpatialKind">The spatial field kind, when this is a spatial field.</param>
public sealed record IndexInspectionField(
    string Name,
    bool Indexed,
    bool Stored,
    bool HasDocValues,
    bool HasTermVectors,
    bool HasOrdinalDocValues,
    int SegmentCount,
    int DocumentCount,
    long? UniqueTermCount,
    long? TotalTermFrequency,
    int? VectorDimension,
    bool? VectorNormalised,
    bool? HasHnsw,
    int? HnswNodeCount,
    VectorQuantisation? VectorQuantisation,
    SpatialFieldKind? SpatialKind)
{
    /// <summary>Gets whether a Float32 vector graph (<c>.vgr</c>) exists for this field, when this is a vector field.</summary>
    public bool? HasVectorGraph { get; init; }

    /// <summary>Gets the total node count of the field's usable vector graphs, when this is a vector field.</summary>
    public int? VectorGraphNodeCount { get; init; }
}

/// <summary>A term and its aggregate live-document statistics.</summary>
/// <param name="Term">The unqualified term text.</param>
/// <param name="DocumentFrequency">The number of live documents containing the term.</param>
/// <param name="TotalTermFrequency">The total frequency across live documents.</param>
public sealed record IndexInspectionTerm(string Term, int DocumentFrequency, long TotalTermFrequency);

/// <summary>A live posting and its positions when positional data is available.</summary>
/// <param name="DocumentId">The global document identifier.</param>
/// <param name="SegmentId">The containing segment identifier.</param>
/// <param name="SegmentDocumentId">The document identifier within the segment.</param>
/// <param name="Frequency">The term frequency for the document.</param>
/// <param name="Positions">The term positions, or an empty list when unavailable.</param>
public sealed record IndexInspectionPosting(
    int DocumentId,
    string SegmentId,
    int SegmentDocumentId,
    int Frequency,
    IReadOnlyList<int> Positions);

/// <summary>A committed document and the values that can be safely inspected.</summary>
/// <param name="DocumentId">The global document identifier.</param>
/// <param name="SegmentId">The containing segment identifier.</param>
/// <param name="SegmentDocumentId">The document identifier within the segment.</param>
/// <param name="IsLive">Whether the document is live in the committed snapshot.</param>
/// <param name="StoredFields">Text stored-field values.</param>
/// <param name="BinaryFieldLengths">Binary stored-field byte lengths.</param>
/// <param name="BinaryFieldHashes">SHA-256 fingerprints of binary stored-field values.</param>
/// <param name="DocValues">Typed DocValues values available for the document.</param>
/// <param name="Vectors">Vector values keyed by field name.</param>
public sealed record IndexInspectionDocument(
    int DocumentId,
    string SegmentId,
    int SegmentDocumentId,
    bool IsLive,
    IReadOnlyDictionary<string, IReadOnlyList<string>> StoredFields,
    IReadOnlyDictionary<string, IReadOnlyList<int>> BinaryFieldLengths,
    IReadOnlyDictionary<string, IReadOnlyList<float>> Vectors,
    IReadOnlyDictionary<string, IReadOnlyList<string>> BinaryFieldHashes,
    IReadOnlyList<IndexInspectionDocValue> DocValues);

/// <summary>A document-level DocValues entry with a stable type name and canonical values.</summary>
/// <param name="FieldName">The DocValues field.</param>
/// <param name="Kind">The DocValues kind.</param>
/// <param name="Values">Canonical values in document order.</param>
public sealed record IndexInspectionDocValue(string FieldName, string Kind, IReadOnlyList<string> Values);

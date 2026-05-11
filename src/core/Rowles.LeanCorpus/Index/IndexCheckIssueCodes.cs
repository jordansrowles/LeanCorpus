namespace Rowles.LeanCorpus.Index;

/// <summary>
/// Stable issue codes emitted by <see cref="IndexValidator"/>.
/// </summary>
public static class IndexCheckIssueCodes
{
    /// <summary>Compatibility code used for issues added through the legacy internal API.</summary>
    public const string LegacyIssue = "LLIDX000";

    /// <summary>No commit file was found.</summary>
    public const string NoCommitFile = "LLIDX001";

    /// <summary>A commit file could not be read.</summary>
    public const string CommitUnreadable = "LLIDX002";

    /// <summary>A commit file failed CRC validation.</summary>
    public const string CommitCrcMismatch = "LLIDX003";

    /// <summary>A commit file contains invalid JSON.</summary>
    public const string CommitInvalidJson = "LLIDX004";

    /// <summary>A commit file generation does not match its file name.</summary>
    public const string CommitGenerationMismatch = "LLIDX005";

    /// <summary>A required segment file is missing.</summary>
    public const string RequiredFileMissing = "LLIDX006";

    /// <summary>A required segment file is empty.</summary>
    public const string RequiredFileEmpty = "LLIDX007";

    /// <summary>Segment metadata could not be read.</summary>
    public const string SegmentMetadataUnreadable = "LLIDX008";

    /// <summary>Segment metadata ID does not match the referenced segment ID.</summary>
    public const string SegmentIdMismatch = "LLIDX009";

    /// <summary>Segment document count is invalid.</summary>
    public const string InvalidDocCount = "LLIDX010";

    /// <summary>Segment live-document count is invalid.</summary>
    public const string InvalidLiveDocCount = "LLIDX011";

    /// <summary>A codec file has invalid magic.</summary>
    public const string InvalidCodecMagic = "LLIDX012";

    /// <summary>A codec file uses an unsupported version.</summary>
    public const string UnsupportedCodecVersion = "LLIDX013";

    /// <summary>A stored-field file has an invalid header.</summary>
    public const string InvalidStoredFieldHeader = "LLIDX014";

    /// <summary>A stored-field file uses an unsupported version.</summary>
    public const string UnsupportedStoredFieldVersion = "LLIDX015";

    /// <summary>A stored-field file uses an unregistered compression policy.</summary>
    public const string UnregisteredCompressionPolicy = "LLIDX016";

    /// <summary>A stored-field index document count differs from segment metadata.</summary>
    public const string StoredFieldDocCountMismatch = "LLIDX017";

    /// <summary>A stored-field index contains invalid block offsets.</summary>
    public const string InvalidStoredFieldOffsets = "LLIDX018";

    /// <summary>A deletion generation file is missing for a segment with deletions.</summary>
    public const string DeletionFileMissing = "LLIDX019";

    /// <summary>A deletion file could not be read.</summary>
    public const string DeletionFileUnreadable = "LLIDX020";

    /// <summary>A deletion file live count differs from segment metadata.</summary>
    public const string DeletionLiveCountMismatch = "LLIDX021";

    /// <summary>A vector file declared by segment metadata is missing.</summary>
    public const string VectorFileMissing = "LLIDX022";

    /// <summary>A vector file has an invalid header.</summary>
    public const string InvalidVectorHeader = "LLIDX023";

    /// <summary>A vector file document count differs from segment metadata.</summary>
    public const string VectorCountMismatch = "LLIDX024";

    /// <summary>A vector file dimension differs from segment metadata.</summary>
    public const string VectorDimensionMismatch = "LLIDX025";

    /// <summary>An HNSW file declared by segment metadata is missing.</summary>
    public const string HnswFileMissing = "LLIDX026";

    /// <summary>An HNSW file has an invalid header.</summary>
    public const string InvalidHnswHeader = "LLIDX027";

    /// <summary>An HNSW file dimension differs from segment metadata.</summary>
    public const string HnswDimensionMismatch = "LLIDX028";

    /// <summary>An HNSW file normalisation flag differs from segment metadata.</summary>
    public const string HnswNormalisationMismatch = "LLIDX029";

    /// <summary>A DocValues file could not be read.</summary>
    public const string DocValuesReadFailure = "LLIDX030";

    /// <summary>A DocValues field document count differs from segment metadata.</summary>
    public const string DocValuesDocCountMismatch = "LLIDX031";

    /// <summary>Stored fields could not be read during deep validation.</summary>
    public const string StoredFieldsReadFailure = "LLIDX032";

    /// <summary>Postings could not be read or contain invalid document IDs during deep validation.</summary>
    public const string PostingsReadFailure = "LLIDX033";

    /// <summary>Vectors could not be read during deep validation.</summary>
    public const string VectorReadFailure = "LLIDX034";

    /// <summary>HNSW graph data could not be read during deep validation.</summary>
    public const string HnswReadFailure = "LLIDX035";

    /// <summary>A codec file uses a future version unsupported by this build.</summary>
    public const string UnsupportedFutureCodecVersion = "LLIDX036";

    /// <summary>An index must be migrated before the requested operation can continue.</summary>
    public const string MigrationRequired = "LLIDX037";

    /// <summary>An index can be opened but migration is recommended.</summary>
    public const string MigrationRecommended = "LLIDX038";

    /// <summary>A migration marker shows that migration is currently in progress.</summary>
    public const string MigrationInProgress = "LLIDX039";

    /// <summary>A codec migration path is not supported by this build.</summary>
    public const string UnsupportedMigrationPath = "LLIDX040";

    /// <summary>A recognised temporary file was left behind by an interrupted write.</summary>
    public const string StaleTemporaryFile = "LLIDX041";

    /// <summary>A migration marker cannot be read or describes partial migration state.</summary>
    public const string PartialMigrationMarkerState = "LLIDX042";

    /// <summary>A migration succeeded but its staging directory could not be removed.</summary>
    public const string MigrationStagingCleanupFailed = "LLIDX043";
}

namespace Rowles.LeanCorpus.Index;

internal static class IndexRepairRecommendations
{
    public static IReadOnlyList<string> ForIssue(string code)
        => code switch
        {
            IndexCheckIssueCodes.NoCommitFile =>
                ["Restore a backup or create a fresh index before opening the directory."],

            IndexCheckIssueCodes.CommitUnreadable or
            IndexCheckIssueCodes.CommitCrcMismatch or
            IndexCheckIssueCodes.CommitInvalidJson or
            IndexCheckIssueCodes.CommitGenerationMismatch =>
                ["Restore a known-good commit file from backup, or use index recovery tooling to fall back to the latest readable commit."],

            IndexCheckIssueCodes.RequiredFileMissing or
            IndexCheckIssueCodes.RequiredFileEmpty =>
                ["Restore the missing or empty segment file from backup, or rebuild the affected segment from source documents."],

            IndexCheckIssueCodes.SegmentMetadataUnreadable or
            IndexCheckIssueCodes.SegmentIdMismatch or
            IndexCheckIssueCodes.InvalidDocCount or
            IndexCheckIssueCodes.InvalidLiveDocCount =>
                ["Restore or rebuild the affected segment metadata before searching or writing to the index."],

            IndexCheckIssueCodes.InvalidCodecMagic =>
                ["Restore the affected codec file from backup, or rebuild the affected segment from source documents."],

            IndexCheckIssueCodes.UnsupportedCodecVersion or
            IndexCheckIssueCodes.UnsupportedFutureCodecVersion =>
                ["Open the index with a newer LeanCorpus build that supports this codec version, or restore an index written by a supported build."],

            IndexCheckIssueCodes.InvalidStoredFieldHeader or
            IndexCheckIssueCodes.UnsupportedStoredFieldVersion or
            IndexCheckIssueCodes.UnregisteredCompressionPolicy or
            IndexCheckIssueCodes.StoredFieldDocCountMismatch or
            IndexCheckIssueCodes.InvalidStoredFieldOffsets or
            IndexCheckIssueCodes.StoredFieldsReadFailure =>
                ["Restore stored-field files from backup, rebuild stored fields from source documents, or run codec migration when the format is older and supported."],

            IndexCheckIssueCodes.DeletionFileMissing or
            IndexCheckIssueCodes.DeletionFileUnreadable or
            IndexCheckIssueCodes.DeletionLiveCountMismatch =>
                ["Restore the deletion file from backup, or rebuild the affected segment so live-document counts match segment metadata."],

            IndexCheckIssueCodes.VectorFileMissing or
            IndexCheckIssueCodes.InvalidVectorHeader or
            IndexCheckIssueCodes.VectorCountMismatch or
            IndexCheckIssueCodes.VectorDimensionMismatch or
            IndexCheckIssueCodes.VectorReadFailure =>
                ["Restore vector files from backup, or rebuild vectors for the affected segment from source documents."],

            IndexCheckIssueCodes.HnswFileMissing or
            IndexCheckIssueCodes.InvalidHnswHeader or
            IndexCheckIssueCodes.HnswDimensionMismatch or
            IndexCheckIssueCodes.HnswNormalisationMismatch or
            IndexCheckIssueCodes.HnswReadFailure =>
                ["Restore or rebuild the HNSW graph for the affected vector field."],

            IndexCheckIssueCodes.DocValuesReadFailure or
            IndexCheckIssueCodes.DocValuesDocCountMismatch =>
                ["Restore DocValues files from backup, rebuild DocValues for the affected segment, or run codec migration when the format is older and supported."],

            IndexCheckIssueCodes.PostingsReadFailure =>
                ["Restore postings and term dictionary files from backup, rebuild the affected segment, or run codec migration when the postings format is older and supported."],

            IndexCheckIssueCodes.MigrationRequired or
            IndexCheckIssueCodes.MigrationRecommended =>
                ["Run IndexCodecMigrator.Plan first, then run IndexCodecMigrator.Migrate after reviewing the planned actions."],

            IndexCheckIssueCodes.MigrationInProgress =>
                ["Inspect the migration marker and either roll back, publish, or abandon the interrupted migration before opening the index."],

            IndexCheckIssueCodes.UnsupportedMigrationPath =>
                ["Restore from backup or rebuild the affected segment because this build cannot rewrite the old codec format automatically."],

            IndexCheckIssueCodes.StaleTemporaryFile =>
                ["Confirm no writer or migration is running, then remove the stale temporary file."],

            IndexCheckIssueCodes.PartialMigrationMarkerState =>
                ["Inspect the migration marker manually and abandon or recreate the migration state before opening the index."],

            IndexCheckIssueCodes.MigrationStagingCleanupFailed =>
                ["Confirm no process is using the staging directory, then remove it manually."],

            _ => []
        };
}

using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Compatibility;

internal static class IndexOpenGuard
{
    public static void EnsureNoBlockingMigration(MMapDirectory directory, IndexOpenCompatibilityMode mode)
    {
        if (mode == IndexOpenCompatibilityMode.UnsafeIgnoreCompatibility)
            return;

        if (IndexMigrationRecovery.HasBlockingMarker(directory.DirectoryPath))
            throw new InvalidDataException($"Index at '{directory.DirectoryPath}' has an incomplete migration marker. Roll back or abandon the migration before opening it.");
    }

    public static void EnsureCanOpenSegments(
        MMapDirectory directory,
        IEnumerable<string> segmentIds,
        IndexOpenCompatibilityMode mode,
        bool forWriting,
        CodecCatalog? catalog = null)
    {
        if (mode == IndexOpenCompatibilityMode.UnsafeIgnoreCompatibility)
            return;

        // Searchers validate the commit, segment metadata, required file presence,
        // and migration marker while opening. Codec headers are validated by the
        // relevant lazy component on first use. Writers retain the eager scan so
        // they cannot append to an index that requires migration.
        if (!forWriting)
            return;

        catalog ??= CodecCatalog.Default;

        var migrationRecommended = false;
        foreach (var segmentId in segmentIds)
        {
            var inspection = IndexFormatInspector.InspectSegmentFiles(
                directory,
                segmentId,
                new IndexFormatInspectionOptions
                {
                    IncludeOptionalSidecars = true,
                    IncludeFileSizes = false,
                    IncludeChecksums = false,
                    Catalog = catalog
                });

            var blockingIssue = inspection.Issues.FirstOrDefault(static issue => issue.Severity == IndexCheckSeverity.Error);
            if (blockingIssue is not null)
            {
                throw new InvalidDataException(
                    $"Index at '{directory.DirectoryPath}' contains incompatible codec file '{blockingIssue.FileName}': {blockingIssue.Message}");
            }

            if (inspection.Files.Any(static file =>
                    file.CurrentFormatVersion.HasValue &&
                    file.IsSupported &&
                    !file.IsCurrent))
            {
                migrationRecommended = true;
            }
        }

        if (migrationRecommended)
            throw new InvalidDataException($"Index at '{directory.DirectoryPath}' contains supported older codec files. Migrate the index before opening it for writing.");
    }
}

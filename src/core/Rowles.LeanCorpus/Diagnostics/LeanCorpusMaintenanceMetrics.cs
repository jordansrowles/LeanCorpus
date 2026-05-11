using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Rowles.LeanCorpus.Diagnostics;

internal static class LeanCorpusMaintenanceMetrics
{
    private static readonly Meter Meter = new("Rowles.LeanCorpus");

    private static readonly Counter<long> FormatInspectCount = Meter.CreateCounter<long>(
        "leancorpus.index.format.inspect.count",
        unit: "{operation}",
        description: "Total number of index format inspection operations.");

    private static readonly Histogram<double> FormatInspectDuration = Meter.CreateHistogram<double>(
        "leancorpus.index.format.inspect.duration",
        unit: "ms",
        description: "Elapsed time for each index format inspection operation.");

    private static readonly Counter<long> CodecMigrationPlanCount = Meter.CreateCounter<long>(
        "leancorpus.index.codec_migration.plan.count",
        unit: "{operation}",
        description: "Total number of codec migration planning operations.");

    private static readonly Histogram<double> CodecMigrationPlanDuration = Meter.CreateHistogram<double>(
        "leancorpus.index.codec_migration.plan.duration",
        unit: "ms",
        description: "Elapsed time for each codec migration planning operation.");

    private static readonly Counter<long> CodecMigrationMigrateCount = Meter.CreateCounter<long>(
        "leancorpus.index.codec_migration.migrate.count",
        unit: "{operation}",
        description: "Total number of codec migration execution operations.");

    private static readonly Histogram<double> CodecMigrationMigrateDuration = Meter.CreateHistogram<double>(
        "leancorpus.index.codec_migration.migrate.duration",
        unit: "ms",
        description: "Elapsed time for each codec migration execution operation.");

    private static readonly Counter<long> BackupManifestCount = Meter.CreateCounter<long>(
        "leancorpus.index.backup.manifest.count",
        unit: "{operation}",
        description: "Total number of backup manifest creation operations.");

    private static readonly Histogram<double> BackupManifestDuration = Meter.CreateHistogram<double>(
        "leancorpus.index.backup.manifest.duration",
        unit: "ms",
        description: "Elapsed time for each backup manifest creation operation.");

    private static readonly Counter<long> BackupCopyCount = Meter.CreateCounter<long>(
        "leancorpus.index.backup.copy.count",
        unit: "{operation}",
        description: "Total number of backup copy operations.");

    private static readonly Histogram<double> BackupCopyDuration = Meter.CreateHistogram<double>(
        "leancorpus.index.backup.copy.duration",
        unit: "ms",
        description: "Elapsed time for each backup copy operation.");

    private static readonly Counter<long> BackupValidateCount = Meter.CreateCounter<long>(
        "leancorpus.index.backup.validate.count",
        unit: "{operation}",
        description: "Total number of backup validation operations.");

    private static readonly Histogram<double> BackupValidateDuration = Meter.CreateHistogram<double>(
        "leancorpus.index.backup.validate.duration",
        unit: "ms",
        description: "Elapsed time for each backup validation operation.");

    private static readonly Counter<long> BackupRestoreCount = Meter.CreateCounter<long>(
        "leancorpus.index.backup.restore.count",
        unit: "{operation}",
        description: "Total number of backup restore operations.");

    private static readonly Histogram<double> BackupRestoreDuration = Meter.CreateHistogram<double>(
        "leancorpus.index.backup.restore.duration",
        unit: "ms",
        description: "Elapsed time for each backup restore operation.");

    internal static void RecordFormatInspect(TimeSpan elapsed, bool succeeded)
    {
        var tags = CreateSuccessTags(succeeded);
        FormatInspectCount.Add(1, tags);
        FormatInspectDuration.Record(elapsed.TotalMilliseconds, tags);
    }

    internal static void RecordCodecMigrationPlan(TimeSpan elapsed, bool succeeded)
    {
        var tags = CreateSuccessTags(succeeded);
        CodecMigrationPlanCount.Add(1, tags);
        CodecMigrationPlanDuration.Record(elapsed.TotalMilliseconds, tags);
    }

    internal static void RecordCodecMigrationMigrate(TimeSpan elapsed, bool succeeded, bool dryRun, bool usesStaging)
    {
        var tags = CreateSuccessTags(succeeded);
        tags.Add("index.migration.dry_run", dryRun);
        tags.Add("index.migration.uses_staging", usesStaging);
        CodecMigrationMigrateCount.Add(1, tags);
        CodecMigrationMigrateDuration.Record(elapsed.TotalMilliseconds, tags);
    }

    internal static void RecordBackupManifest(TimeSpan elapsed, bool succeeded, bool includeCommitStats)
    {
        var tags = CreateSuccessTags(succeeded);
        tags.Add("index.backup.include_commit_stats", includeCommitStats);
        BackupManifestCount.Add(1, tags);
        BackupManifestDuration.Record(elapsed.TotalMilliseconds, tags);
    }

    internal static void RecordBackupCopy(TimeSpan elapsed, bool succeeded, bool overwrite)
    {
        var tags = CreateSuccessTags(succeeded);
        tags.Add("index.backup.overwrite", overwrite);
        BackupCopyCount.Add(1, tags);
        BackupCopyDuration.Record(elapsed.TotalMilliseconds, tags);
    }

    internal static void RecordBackupValidate(TimeSpan elapsed, bool succeeded)
    {
        var tags = CreateSuccessTags(succeeded);
        BackupValidateCount.Add(1, tags);
        BackupValidateDuration.Record(elapsed.TotalMilliseconds, tags);
    }

    internal static void RecordBackupRestore(
        TimeSpan elapsed,
        bool succeeded,
        bool validateAfterRestore,
        bool restoreCommitStats,
        bool overwrite)
    {
        var tags = CreateSuccessTags(succeeded);
        tags.Add("index.restore.validate_after_restore", validateAfterRestore);
        tags.Add("index.restore.restore_commit_stats", restoreCommitStats);
        tags.Add("index.restore.overwrite", overwrite);
        BackupRestoreCount.Add(1, tags);
        BackupRestoreDuration.Record(elapsed.TotalMilliseconds, tags);
    }

    private static TagList CreateSuccessTags(bool succeeded)
    {
        var tags = new TagList();
        tags.Add("operation.succeeded", succeeded);
        return tags;
    }
}

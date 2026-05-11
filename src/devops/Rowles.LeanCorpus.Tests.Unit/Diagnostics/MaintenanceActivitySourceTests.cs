using System.Collections.Concurrent;
using System.Diagnostics;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Unit.Diagnostics;

public sealed class MaintenanceActivitySourceTests : IDisposable
{
    private const string SourceName = "Rowles.LeanCorpus";
    private const string TestSourceName = "Rowles.LeanCorpus.Tests.MaintenanceActivityScope";

    private readonly string _root;
    private readonly ConcurrentBag<Activity> _captured = [];
    private readonly ActivityListener _listener;
    private readonly ActivitySource _testSource = new(TestSourceName);

    public MaintenanceActivitySourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "maintenance_activity_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName || source.Name == TestSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _captured.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _testSource.Dispose();
        foreach (var activity in _captured)
            activity.Dispose();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact(DisplayName = "Format Inspect: Emits Maintenance Activity")]
    public void FormatInspect_EmitsMaintenanceActivity()
    {
        var indexPath = CreateIndex("inspect");

        using var scope = StartScope();
        _ = IndexFormatInspector.Inspect(new MMapDirectory(indexPath));

        var activity = GetActivity(scope, LeanCorpusActivitySource.FormatInspect);
        Assert.Equal(true, activity.GetTagItem("operation.succeeded"));
        Assert.Equal(1, activity.GetTagItem("index.segment_count"));
        Assert.Equal(0, activity.GetTagItem("index.issue_count"));
        AssertNoPathTags(activity);
    }

    [Fact(DisplayName = "Codec Migration Plan: Emits Maintenance Activity")]
    public void CodecMigrationPlan_EmitsMaintenanceActivity()
    {
        var indexPath = CreateIndex("migration-plan");

        using var scope = StartScope();
        _ = IndexCodecMigrator.Plan(new MMapDirectory(indexPath));

        var activity = GetActivity(scope, LeanCorpusActivitySource.CodecMigrationPlan);
        Assert.Equal(true, activity.GetTagItem("operation.succeeded"));
        Assert.NotNull(activity.GetTagItem("index.migration.action_count"));
        Assert.NotNull(activity.GetTagItem("index.migration.can_execute"));
        AssertNoPathTags(activity);
    }

    [Fact(DisplayName = "Codec Migration Migrate: Emits Maintenance Activity")]
    public void CodecMigrationMigrate_EmitsMaintenanceActivity()
    {
        var indexPath = CreateIndex("migration-migrate");

        using var scope = StartScope();
        _ = IndexCodecMigrator.Migrate(new MMapDirectory(indexPath), new IndexCodecMigrationOptions { DryRun = true });

        var activity = GetActivity(scope, LeanCorpusActivitySource.CodecMigrationMigrate);
        Assert.Equal(true, activity.GetTagItem("operation.succeeded"));
        Assert.Equal(true, activity.GetTagItem("index.migration.dry_run"));
        Assert.Equal(true, activity.GetTagItem("index.migration.succeeded"));
        AssertNoPathTags(activity);
    }

    [Fact(DisplayName = "Backup Manifest: Emits Maintenance Activity")]
    public void BackupManifest_EmitsMaintenanceActivity()
    {
        var indexPath = CreateIndex("backup-manifest");

        using var scope = StartScope();
        _ = IndexBackup.CreateManifest(indexPath);

        var activity = GetActivity(scope, LeanCorpusActivitySource.BackupManifest);
        Assert.Equal(true, activity.GetTagItem("operation.succeeded"));
        Assert.NotNull(activity.GetTagItem("index.commit_generation"));
        Assert.NotNull(activity.GetTagItem("index.backup.file_count"));
        Assert.NotNull(activity.GetTagItem("index.backup.byte_count"));
        AssertNoPathTags(activity);
    }

    [Fact(DisplayName = "Backup Copy: Emits Maintenance Activity")]
    public void BackupCopy_EmitsMaintenanceActivity()
    {
        var indexPath = CreateIndex("backup-copy");
        var backupPath = Path.Combine(_root, "backup-copy-target");

        using var scope = StartScope();
        _ = IndexBackup.Backup(indexPath, backupPath);

        var activity = GetActivity(scope, LeanCorpusActivitySource.BackupCopy);
        Assert.Equal(true, activity.GetTagItem("operation.succeeded"));
        Assert.NotNull(activity.GetTagItem("index.backup.file_count"));
        Assert.Equal(false, activity.GetTagItem("index.backup.overwrite"));
        AssertNoPathTags(activity);
    }

    [Fact(DisplayName = "Backup Validate: Emits Maintenance Activity")]
    public void BackupValidate_EmitsMaintenanceActivity()
    {
        var (_, backupPath) = CreateBackup("backup-validate");

        using var scope = StartScope();
        _ = IndexBackup.ValidateBackup(backupPath);

        var activity = GetActivity(scope, LeanCorpusActivitySource.BackupValidate);
        Assert.Equal(true, activity.GetTagItem("operation.succeeded"));
        Assert.NotNull(activity.GetTagItem("index.backup.file_count"));
        AssertNoPathTags(activity);
    }

    [Fact(DisplayName = "Backup Restore: Emits Maintenance Activity")]
    public void BackupRestore_EmitsMaintenanceActivity()
    {
        var (_, backupPath) = CreateBackup("backup-restore");
        var restorePath = Path.Combine(_root, "restore-target");

        using var scope = StartScope();
        _ = IndexBackup.Restore(backupPath, restorePath);

        var activity = GetActivity(scope, LeanCorpusActivitySource.BackupRestore);
        Assert.Equal(true, activity.GetTagItem("operation.succeeded"));
        Assert.NotNull(activity.GetTagItem("index.restore.file_count"));
        Assert.Equal(true, activity.GetTagItem("index.restore.validate_after_restore"));
        AssertNoPathTags(activity);
    }

    private Activity StartScope([System.Runtime.CompilerServices.CallerMemberName] string name = "")
        => _testSource.StartActivity(name) ?? throw new InvalidOperationException("Test scope activity could not be started.");

    private Activity GetActivity(Activity scope, string operationName)
        => _captured.First(activity => activity.RootId == scope.RootId &&
            activity.Source.Name == SourceName &&
            activity.OperationName == operationName);

    private static void AssertNoPathTags(Activity activity)
    {
        Assert.DoesNotContain(activity.TagObjects, tag =>
            tag.Key.Contains("path", StringComparison.OrdinalIgnoreCase) ||
            tag.Key.Contains("directory", StringComparison.OrdinalIgnoreCase));
    }

    private (string IndexPath, string BackupPath) CreateBackup(string name)
    {
        var indexPath = CreateIndex(name + "-index");
        var backupPath = Path.Combine(_root, name + "-backup");
        IndexBackup.Backup(indexPath, backupPath);
        return (indexPath, backupPath);
    }

    private string CreateIndex(string name)
    {
        var indexPath = Path.Combine(_root, name);
        Directory.CreateDirectory(indexPath);
        using var writer = new IndexWriter(new MMapDirectory(indexPath), new IndexWriterConfig());
        var document = new LeanDocument();
        document.Add(new TextField("body", "maintenance telemetry test"));
        writer.AddDocument(document);
        writer.Commit();
        return indexPath;
    }
}

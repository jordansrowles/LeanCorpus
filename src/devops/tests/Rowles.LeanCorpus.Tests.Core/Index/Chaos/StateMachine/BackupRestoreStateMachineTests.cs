using FsCheck;
using FsCheck.Experimental;
using Microsoft.FSharp.Core;
using Rowles.LeanCorpus.Tests.Core.Infrastructure;
using FsCheckStateMachine = FsCheck.Experimental.StateMachine;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

[Category(TestCategory.Chaos)]
[Area(TestArea.Index)]
[Technique(TestTechnique.PropertyBased)]
[Technique(TestTechnique.StateMachine)]
public sealed class BackupRestoreStateMachineTests
{
    [Fact(DisplayName = "Backup and restore preserves committed snapshots and chain integrity")]
    public void Backup_restore_operations_match_the_model()
    {
        string? output = Environment.GetEnvironmentVariable("LEANCORPUS_BACKUP_RESTORE_MEASUREMENT");
        ulong seed = output is null ? (ulong)Random.Shared.NextInt64() : 14854304604442296793UL;
        const ulong gamma = 3998140771831394483UL;
        using var measurement = output is null ? null : new BackupRestoreMeasurement(output, seed, gamma);
        var config = Config.QuickThrowOnFailure
            .WithMaxTest(ChaosConfig.FsCheckIterations)
            .WithStartSize(1).WithEndSize(30)
            .WithReplay(FSharpOption<Replay>.Some(new Replay(new Rnd(seed, gamma), FSharpOption<int>.None)));
        Check.One(config, FsCheckStateMachine.ToProperty(new BackupRestoreMachine()));
        if (measurement is not null)
            measurement.Passed = true;
    }

    [Fact(DisplayName = "Backup and restore preserves a full and incremental chain")]
    public void Full_and_incremental_chain_restores_latest_snapshot()
    {
        using var harness = new BackupRestoreHarness();
        var model = BackupRestoreModel.Empty;
        var first = ModelDocument.Create(0);
        var second = ModelDocument.Create(1);

        harness.Add(first);
        model = model.Add(first);
        harness.Commit();
        model = model.Commit();

        var full = model.AddFullBackup(0);
        var fullResult = harness.CreateFullBackup(0, full.GetBackup(0).Snapshot.Generation);
        Assert.Equal(IndexBackupKind.Full, fullResult.Manifest.Kind);
        model = full;

        harness.Add(second);
        model = model.Add(second);
        harness.Commit();
        model = model.Commit();

        var incremental = model.AddIncrementalBackup(1, 0);
        var incrementalResult = harness.CreateIncrementalBackup(
            1,
            0,
            incremental.GetBackup(1).Snapshot.Generation);
        Assert.Equal(IndexBackupKind.Incremental, incrementalResult.Manifest.Kind);
        model = incremental;

        var restored = harness.Restore(model.GetBackup(1), model.NextRestoreId);
        harness.AssertRestored(restored, model.GetBackup(1).Snapshot);
    }

    [Fact(DisplayName = "Damaged backup restore leaves the existing target untouched")]
    public void Damaged_backup_does_not_publish_partial_restore()
    {
        using var harness = new BackupRestoreHarness();
        var model = BackupRestoreModel.Empty.Add(ModelDocument.Create(0)).Commit();
        model = model.AddFullBackup(0);
        harness.Add(ModelDocument.Create(0));
        harness.Commit();
        harness.CreateFullBackup(0, model.GetBackup(0).Snapshot.Generation);

        var damaged = model.MarkCorrupt(0);
        harness.CorruptAndAssertRestoreFails(damaged.GetBackup(0), damaged.NextRestoreId, removeFile: false);
    }

    [Fact(DisplayName = "Standalone incremental validation requires its parent chain")]
    public void Standalone_incremental_validation_is_rejected()
    {
        using var harness = new BackupRestoreHarness();
        var document = ModelDocument.Create(0);
        var model = BackupRestoreModel.Empty;
        harness.Add(document);
        model = model.Add(document);
        harness.Commit();
        model = model.Commit();
        harness.CreateFullBackup(0, model.History.Latest.Generation);
        model = model.AddFullBackup(0);

        var second = ModelDocument.Create(1);
        harness.Add(second);
        model = model.Add(second);
        harness.Commit();
        model = model.Commit();
        harness.CreateIncrementalBackup(1, 0, model.History.Latest.Generation);
        model = model.AddIncrementalBackup(1, 0);

        harness.ValidateStandaloneIncremental(model.GetBackup(1));
    }
}

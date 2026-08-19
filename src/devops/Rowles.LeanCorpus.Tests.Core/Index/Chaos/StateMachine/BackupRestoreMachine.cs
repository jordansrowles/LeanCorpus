using FsCheck;
using FsCheck.Experimental;
using FsCheck.Fluent;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal sealed class BackupRestoreMachine : Machine<BackupRestoreHarness, BackupRestoreModel>
{
    private const int MaxTraceLength = 30;

    private static readonly IReadOnlyList<Operation<BackupRestoreHarness, BackupRestoreModel>> SearchOperations =
        SearchSpec.Cases
            .Select(static search => (Operation<BackupRestoreHarness, BackupRestoreModel>)new BackupSourceSearchOperation(search))
            .ToArray();

    public BackupRestoreMachine()
        : base(MaxTraceLength)
    {
    }

    public override Arbitrary<Setup<BackupRestoreHarness, BackupRestoreModel>> Setup =>
        Arb.ToArbitrary(Gen.Fresh(static () =>
            (Setup<BackupRestoreHarness, BackupRestoreModel>)new BackupRestoreSetup()));

    public override Gen<Operation<BackupRestoreHarness, BackupRestoreModel>> Next(BackupRestoreModel model)
    {
        var choices = new List<(int, Gen<Operation<BackupRestoreHarness, BackupRestoreModel>>)>
        {
            (15, Constant(new BackupAddOperation(ModelDocument.Create(model.NextId)))),
            (10, Constant(new BackupAddBatchOperation(CreateBatch(model.NextId)))),
            (16, Constant(new BackupCommitOperation())),
            (8, Gen.Elements(SearchOperations.ToArray())),
            (12, Constant(new FullBackupOperation(model.NextBackupId)))
        };

        if (model.Working.Count > 0)
        {
            var deletes = model.Working.Keys
                .OrderBy(static id => id, StringComparer.Ordinal)
                .Select(static id => (Operation<BackupRestoreHarness, BackupRestoreModel>)new BackupDeleteOperation(id))
                .ToArray();
            choices.Add((8, Gen.Elements(deletes)));
        }

        var updates = model.Working.Values
            .Where(document => model.History.Latest.Contains(document.Id))
            .OrderBy(static document => document.Id, StringComparer.Ordinal)
            .Select(static document => (Operation<BackupRestoreHarness, BackupRestoreModel>)new BackupUpdateOperation(document.Replacement()))
            .ToArray();
        if (updates.Length > 0)
            choices.Add((8, Gen.Elements(updates)));

        var incrementalParents = model.Backups
            .Where(backup => backup.Healthy && backup.Snapshot.Generation < model.History.Latest.Generation)
            .OrderBy(static backup => backup.Id)
            .ToArray();
        if (incrementalParents.Length > 0)
        {
            var incrementals = incrementalParents
                .Select(parent => (Operation<BackupRestoreHarness, BackupRestoreModel>)new IncrementalBackupOperation(
                    model.NextBackupId,
                    parent.Id))
                .ToArray();
            choices.Add((12, Gen.Elements(incrementals)));
        }

        var healthyBackups = model.Backups
            .Where(static backup => backup.Healthy)
            .OrderBy(static backup => backup.Id)
            .ToArray();
        if (healthyBackups.Length > 0)
        {
            var validates = healthyBackups
                .Select(backup => (Operation<BackupRestoreHarness, BackupRestoreModel>)new ValidateBackupOperation(backup.Id))
                .ToArray();
            var restores = healthyBackups
                .Select(backup => (Operation<BackupRestoreHarness, BackupRestoreModel>)new RestoreBackupOperation(
                    backup.Id,
                    model.NextRestoreId))
                .ToArray();
            choices.Add((8, Gen.Elements(validates)));
            choices.Add((8, Gen.Elements(restores)));

            var standaloneIncrementals = healthyBackups
                .Where(static backup => backup.IsIncremental && backup.RequiresParent)
                .Select(backup => (Operation<BackupRestoreHarness, BackupRestoreModel>)new ValidateStandaloneIncrementalOperation(backup.Id))
                .ToArray();
            if (standaloneIncrementals.Length > 0)
                choices.Add((5, Gen.Elements(standaloneIncrementals)));

            var corruptible = healthyBackups
                .Where(static backup => backup.CanCorrupt)
                .SelectMany(backup => new[]
                {
                    (Operation<BackupRestoreHarness, BackupRestoreModel>)new CorruptBackupOperation(
                        backup.Id, model.NextRestoreId, removeFile: false),
                    new CorruptBackupOperation(backup.Id, model.NextRestoreId, removeFile: true)
                })
                .ToArray();
            if (corruptible.Length > 0)
                choices.Add((6, Gen.Elements(corruptible)));
        }

        return Gen.Frequency(choices.ToArray());
    }

    public override TearDown<BackupRestoreHarness> TearDown => new BackupRestoreTearDown();

    private static Gen<Operation<BackupRestoreHarness, BackupRestoreModel>> Constant(
        BackupRestoreOperation operation) =>
        Gen.Constant<Operation<BackupRestoreHarness, BackupRestoreModel>>(operation);

    private static ModelDocument[] CreateBatch(int firstNumber)
    {
        int count = 1 + firstNumber % 3;
        return Enumerable.Range(firstNumber, count)
            .Select(ModelDocument.Create)
            .ToArray();
    }

    private sealed class BackupRestoreSetup : Setup<BackupRestoreHarness, BackupRestoreModel>
    {
        public override BackupRestoreModel Model() => BackupRestoreModel.Empty;

        public override BackupRestoreHarness Actual() => new();
    }

    private sealed class BackupRestoreTearDown : TearDown<BackupRestoreHarness>
    {
        public override void Actual(BackupRestoreHarness actual) => actual.Dispose();
    }
}

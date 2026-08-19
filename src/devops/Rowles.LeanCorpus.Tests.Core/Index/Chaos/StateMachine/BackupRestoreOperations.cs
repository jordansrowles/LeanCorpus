using FsCheck;
using FsCheck.Experimental;
using FsCheck.Fluent;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal abstract class BackupRestoreOperation : Operation<BackupRestoreHarness, BackupRestoreModel>
{
    protected static Property Succeeds() => Prop.ToProperty(true);
}

internal sealed class BackupAddOperation(ModelDocument document) : BackupRestoreOperation
{
    public override BackupRestoreModel Run(BackupRestoreModel model) => model.Add(document);

    public override Property Check(BackupRestoreHarness actual, BackupRestoreModel model)
    {
        actual.Add(document);
        return Succeeds();
    }

    public override string ToString() => $"Add({document.Id})";
}

internal sealed class BackupAddBatchOperation(IReadOnlyList<ModelDocument> documents) : BackupRestoreOperation
{
    public override BackupRestoreModel Run(BackupRestoreModel model) => model.AddBatch(documents);

    public override Property Check(BackupRestoreHarness actual, BackupRestoreModel model)
    {
        actual.AddBatch(documents);
        return Succeeds();
    }

    public override string ToString() => $"AddBatch([{string.Join(",", documents.Select(static document => document.Id))}])";
}

internal sealed class BackupDeleteOperation(string id) : BackupRestoreOperation
{
    public override bool Pre(BackupRestoreModel model) => model.Working.ContainsKey(id);

    public override BackupRestoreModel Run(BackupRestoreModel model) => model.Delete(id);

    public override Property Check(BackupRestoreHarness actual, BackupRestoreModel model)
    {
        actual.Delete(id);
        return Succeeds();
    }

    public override string ToString() => $"Delete({id})";
}

internal sealed class BackupUpdateOperation(ModelDocument replacement) : BackupRestoreOperation
{
    public override bool Pre(BackupRestoreModel model) =>
        model.Working.ContainsKey(replacement.Id) && model.History.Latest.Contains(replacement.Id);

    public override BackupRestoreModel Run(BackupRestoreModel model) => model.Update(replacement);

    public override Property Check(BackupRestoreHarness actual, BackupRestoreModel model)
    {
        actual.Update(replacement);
        return Succeeds();
    }

    public override string ToString() => $"Update({replacement.Id})";
}

internal sealed class BackupCommitOperation : BackupRestoreOperation
{
    public override BackupRestoreModel Run(BackupRestoreModel model) => model.Commit();

    public override Property Check(BackupRestoreHarness actual, BackupRestoreModel model)
    {
        actual.Commit();
        actual.AssertSourceSearch(new SearchSpec(SearchKind.MatchAll), model.History.Latest.Documents);
        return Succeeds();
    }

    public override string ToString() => "Commit()";
}

internal sealed class BackupSourceSearchOperation(SearchSpec search) : BackupRestoreOperation
{
    public override BackupRestoreModel Run(BackupRestoreModel model) => model;

    public override Property Check(BackupRestoreHarness actual, BackupRestoreModel model)
    {
        actual.AssertSourceSearch(search, model.History.Latest.Documents);
        return Succeeds();
    }

    public override string ToString() => $"SearchSource({search})";
}

internal sealed class FullBackupOperation(int backupId) : BackupRestoreOperation
{
    public override BackupRestoreModel Run(BackupRestoreModel model) => model.AddFullBackup(backupId);

    public override Property Check(BackupRestoreHarness actual, BackupRestoreModel model)
    {
        var expected = model.GetBackup(backupId);
        var result = actual.CreateFullBackup(backupId, expected.Snapshot.Generation);
        Assert.Equal(IndexBackupKind.Full, result.Manifest.Kind);
        Assert.Equal(expected.Snapshot.Generation, result.Manifest.CommitGeneration);
        Assert.Equal(expected.Snapshot.ContentToken, result.Manifest.ContentToken);
        Assert.Equal(expected.Snapshot.Generation, actual.ValidateBackup(expected).CommitGeneration);
        return Succeeds();
    }

    public override string ToString() => $"FullBackup({backupId})";
}

internal sealed class IncrementalBackupOperation(int backupId, int parentId) : BackupRestoreOperation
{
    public override bool Pre(BackupRestoreModel model)
    {
        var parent = model.Backups.FirstOrDefault(backup => backup.Id == parentId);
        return parent is not null
            && parent.Healthy
            && model.History.Latest.Generation > parent.Snapshot.Generation;
    }

    public override BackupRestoreModel Run(BackupRestoreModel model) => model.AddIncrementalBackup(backupId, parentId);

    public override Property Check(BackupRestoreHarness actual, BackupRestoreModel model)
    {
        var expected = model.GetBackup(backupId);
        var result = actual.CreateIncrementalBackup(
            backupId,
            parentId,
            expected.Snapshot.Generation);
        Assert.Equal(IndexBackupKind.Incremental, result.Manifest.Kind);
        Assert.Equal(expected.ChainIds.Count, result.Manifest.ChainDepth);
        Assert.NotNull(result.Manifest.ParentManifestSha256);
        Assert.Equal(expected.Snapshot.Generation, actual.ValidateBackup(expected).CommitGeneration);
        return Succeeds();
    }

    public override string ToString() => $"IncrementalBackup({backupId},parent={parentId})";
}

internal sealed class ValidateBackupOperation(int backupId) : BackupRestoreOperation
{
    public override bool Pre(BackupRestoreModel model) =>
        model.Backups.Any(backup => backup.Id == backupId && backup.Healthy);

    public override BackupRestoreModel Run(BackupRestoreModel model) => model;

    public override Property Check(BackupRestoreHarness actual, BackupRestoreModel model)
    {
        var expected = model.GetBackup(backupId);
        Assert.Equal(expected.Snapshot.Generation, actual.ValidateBackup(expected).CommitGeneration);
        return Succeeds();
    }

    public override string ToString() => $"ValidateBackup({backupId})";
}

internal sealed class ValidateStandaloneIncrementalOperation(int backupId) : BackupRestoreOperation
{
    public override bool Pre(BackupRestoreModel model) =>
        model.Backups.Any(backup =>
            backup.Id == backupId
            && backup.Healthy
            && backup.IsIncremental
            && backup.RequiresParent);

    public override BackupRestoreModel Run(BackupRestoreModel model) => model;

    public override Property Check(BackupRestoreHarness actual, BackupRestoreModel model)
    {
        actual.ValidateStandaloneIncremental(model.GetBackup(backupId));
        return Succeeds();
    }

    public override string ToString() => $"ValidateStandaloneIncremental({backupId})";
}

internal sealed class RestoreBackupOperation(int backupId, int restoreId) : BackupRestoreOperation
{
    public override bool Pre(BackupRestoreModel model) =>
        model.Backups.Any(backup => backup.Id == backupId && backup.Healthy);

    public override BackupRestoreModel Run(BackupRestoreModel model) => model.AllocateRestoreTarget();

    public override Property Check(BackupRestoreHarness actual, BackupRestoreModel model)
    {
        var expected = model.GetBackup(backupId);
        var result = actual.Restore(expected, restoreId);
        actual.AssertRestored(result, expected.Snapshot);
        return Succeeds();
    }

    public override string ToString() => $"RestoreBackup({backupId},target={restoreId})";
}

internal sealed class CorruptBackupOperation(int backupId, int restoreId, bool removeFile) : BackupRestoreOperation
{
    public override bool Pre(BackupRestoreModel model) =>
        model.Backups.Any(backup => backup.Id == backupId && backup.Healthy && backup.CanCorrupt);

    public override BackupRestoreModel Run(BackupRestoreModel model) => model.MarkCorrupt(backupId).AllocateRestoreTarget();

    public override Property Check(BackupRestoreHarness actual, BackupRestoreModel model)
    {
        actual.CorruptAndAssertRestoreFails(model.GetBackup(backupId), restoreId, removeFile);
        return Succeeds();
    }

    public override string ToString() =>
        $"CorruptBackup({backupId},target={restoreId},remove={removeFile})";
}

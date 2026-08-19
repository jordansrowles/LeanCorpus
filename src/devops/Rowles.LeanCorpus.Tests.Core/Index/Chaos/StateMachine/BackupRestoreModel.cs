using System.Collections.Immutable;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal sealed record BackupArtifactModel(
    int Id,
    ImmutableList<int> ChainIds,
    CommitSnapshot Snapshot,
    bool Healthy,
    bool RequiresParent = false)
{
    public bool IsIncremental => ChainIds.Count > 1;

    public bool CanCorrupt => Snapshot.Documents.Count > 0;
}

internal sealed record BackupRestoreModel(
    ImmutableDictionary<string, ModelDocument> Working,
    CommitHistoryModel History,
    ImmutableList<BackupArtifactModel> Backups,
    int NextId,
    int NextBackupId,
    int NextRestoreId)
{
    public static BackupRestoreModel Empty => new(
        CommitHistoryModel.Empty.Latest.Documents,
        CommitHistoryModel.Empty,
        ImmutableList<BackupArtifactModel>.Empty,
        0,
        0,
        0);

    public BackupRestoreModel Add(ModelDocument document) => this with
    {
        Working = Working.SetItem(document.Id, document),
        History = History.MarkChanged(),
        NextId = NextId + 1
    };

    public BackupRestoreModel AddBatch(IReadOnlyList<ModelDocument> documents)
    {
        var working = Working;
        foreach (var document in documents)
            working = working.SetItem(document.Id, document);

        return this with
        {
            Working = working,
            History = History.MarkChanged(),
            NextId = NextId + documents.Count
        };
    }

    public BackupRestoreModel Delete(string id) => this with
    {
        Working = Working.Remove(id),
        History = History.MarkChanged()
    };

    public BackupRestoreModel Update(ModelDocument replacement)
    {
        var working = Working.SetItem(replacement.Id, replacement);

        return this with
        {
            Working = working,
            History = History.ApplyLiveDeletes(working, replacement.Id).MarkChanged()
        };
    }

    public BackupRestoreModel Commit()
    {
        var history = History.ApplyLiveDeletes(Working);
        return this with { History = history.Append(Working) };
    }

    public BackupRestoreModel AddFullBackup(int id) => this with
    {
        Backups = Backups.Add(new BackupArtifactModel(
            id,
            ImmutableList.Create(id),
            History.Latest,
            Healthy: true,
            RequiresParent: false)),
        NextBackupId = Math.Max(NextBackupId, id + 1)
    };

    public BackupRestoreModel AddIncrementalBackup(int id, int parentId)
    {
        var parent = GetBackup(parentId);
        return this with
        {
            Backups = Backups.Add(new BackupArtifactModel(
                id,
                parent.ChainIds.Add(id),
                History.Latest,
                Healthy: true,
                RequiresParent: parent.Snapshot.Documents.Count > 0)),
            NextBackupId = Math.Max(NextBackupId, id + 1)
        };
    }

    public BackupRestoreModel MarkCorrupt(int id) => this with
    {
        Backups = Backups
            .Select(backup => backup.ChainIds.Contains(id) ? backup with { Healthy = false } : backup)
            .ToImmutableList()
    };

    public BackupRestoreModel AllocateRestoreTarget() => this with
    {
        NextRestoreId = NextRestoreId + 1
    };

    public BackupArtifactModel GetBackup(int id) =>
        Backups.First(backup => backup.Id == id);
}

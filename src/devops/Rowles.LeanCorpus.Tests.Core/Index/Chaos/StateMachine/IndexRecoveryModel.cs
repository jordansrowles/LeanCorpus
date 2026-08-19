using System.Collections.Immutable;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal sealed record IndexRecoveryModel(
    ImmutableDictionary<string, ModelDocument> Working,
    CommitHistoryModel History,
    int NextId,
    ImmutableHashSet<int> InvalidCommitGenerations)
{
    public bool InvalidLatestCommitPresent =>
        InvalidCommitGenerations.Any(generation => generation > History.Latest.Generation);

    public static IndexRecoveryModel Empty => new(
        CommitHistoryModel.Empty.Latest.Documents,
        CommitHistoryModel.Empty,
        0,
        ImmutableHashSet<int>.Empty);

    public IndexRecoveryModel Add(ModelDocument document) => this with
    {
        Working = Working.SetItem(document.Id, document),
        History = History.MarkChanged(),
        NextId = NextId + 1
    };

    public IndexRecoveryModel AddBatch(IReadOnlyList<ModelDocument> documents)
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

    public IndexRecoveryModel Delete(string id) => this with
    {
        Working = Working.Remove(id),
        History = History.MarkChanged()
    };

    public IndexRecoveryModel Update(ModelDocument replacement)
    {
        var working = Working.SetItem(replacement.Id, replacement);

        return this with
        {
            Working = working,
            History = History.ApplyLiveDeletes(working, replacement.Id).MarkChanged()
        };
    }

    public IndexRecoveryModel Commit()
    {
        var history = History.ApplyLiveDeletes(Working);
        int generation = history.Latest.Generation + 1;
        return this with
        {
            History = history.Append(Working),
            InvalidCommitGenerations = InvalidCommitGenerations.Remove(generation)
        };
    }

    public IndexRecoveryModel Reopen() => this with
    {
        Working = History.Latest.Documents,
        History = History with { HasPendingChanges = false }
    };

    public IndexRecoveryModel Fallback(bool invalidLatestCommitPresent)
    {
        int removedGeneration = History.Latest.Generation;
        var history = History.RemoveLatest();
        return this with
        {
            History = history,
            Working = history.Latest.Documents,
            InvalidCommitGenerations = invalidLatestCommitPresent
                ? InvalidCommitGenerations.Add(removedGeneration)
                : InvalidCommitGenerations
        };
    }
}

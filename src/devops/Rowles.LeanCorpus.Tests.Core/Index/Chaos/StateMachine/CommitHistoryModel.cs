using System.Collections.Immutable;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal sealed record CommitSnapshot(
    int Generation,
    long ContentToken,
    ImmutableDictionary<string, ModelDocument> Documents)
{
    public bool Contains(string id) => Documents.ContainsKey(id);
}

internal sealed record CommitHistoryModel(
    ImmutableList<CommitSnapshot> Commits,
    bool HasPendingChanges = false)
{
    public static CommitHistoryModel Empty { get; } = new(
        ImmutableList.Create(new CommitSnapshot(1, 0, EmptyDocuments())));

    public CommitSnapshot Latest => Commits[^1];

    public CommitHistoryModel Append(ImmutableDictionary<string, ModelDocument> documents)
    {
        long contentToken = !HasPendingChanges
            ? Latest.ContentToken
            : Latest.ContentToken + 1;

        return this with
        {
            Commits = Commits.Add(new CommitSnapshot(
                Latest.Generation + 1,
                contentToken,
                documents)),
            HasPendingChanges = false
        };
    }

    public CommitHistoryModel MarkChanged() => this with { HasPendingChanges = true };

    public CommitHistoryModel RemoveLatest()
    {
        if (Commits.Count <= 1)
            throw new InvalidOperationException("The baseline commit cannot be removed from the model.");

        return this with
        {
            Commits = Commits.RemoveAt(Commits.Count - 1),
            HasPendingChanges = false
        };
    }

    public CommitHistoryModel ApplyLiveDeletes(
        ImmutableDictionary<string, ModelDocument> working,
        string? replacementId = null)
    {
        var commits = Commits
            .Select(commit => commit with
            {
                Documents = RemoveLiveDocuments(commit.Documents, working, replacementId)
            })
            .ToImmutableList();
        return this with { Commits = commits };
    }

    public static ImmutableDictionary<string, ModelDocument> EmptyDocuments() =>
        ImmutableDictionary.Create<string, ModelDocument>(StringComparer.Ordinal);

    private static ImmutableDictionary<string, ModelDocument> RemoveLiveDocuments(
        ImmutableDictionary<string, ModelDocument> documents,
        ImmutableDictionary<string, ModelDocument> working,
        string? replacementId)
    {
        var visible = documents;
        foreach (string id in documents.Keys)
        {
            if (!working.ContainsKey(id)
                || replacementId is not null && string.Equals(id, replacementId, StringComparison.Ordinal))
                visible = visible.Remove(id);
        }
        return visible;
    }
}

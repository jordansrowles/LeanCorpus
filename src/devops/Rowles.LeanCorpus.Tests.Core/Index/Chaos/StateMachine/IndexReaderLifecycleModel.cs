using System.Collections.Immutable;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal sealed record ReaderLeaseModel(
    int Generation,
    int ReaderVersion,
    ImmutableDictionary<string, ModelDocument> Documents);

internal sealed record IndexReaderLifecycleModel(
    ImmutableDictionary<string, ModelDocument> Working,
    CommitHistoryModel History,
    int ManagerGeneration,
    int ManagerReaderVersion,
    long ManagerContentToken,
    ImmutableDictionary<string, ModelDocument> ManagerDocuments,
    ImmutableDictionary<int, ReaderLeaseModel> Leases,
    int NextId,
    int NextLeaseId)
{
    public static IndexReaderLifecycleModel Empty
    {
        get
        {
            var history = CommitHistoryModel.Empty;
            return new IndexReaderLifecycleModel(
                history.Latest.Documents,
                history,
                history.Latest.Generation,
                0,
                history.Latest.ContentToken,
                history.Latest.Documents,
                ImmutableDictionary<int, ReaderLeaseModel>.Empty,
                0,
                0);
        }
    }

    public IndexReaderLifecycleModel Add(ModelDocument document) => this with
    {
        Working = Working.SetItem(document.Id, document),
        History = History.MarkChanged(),
        NextId = NextId + 1
    };

    public IndexReaderLifecycleModel AddBatch(IReadOnlyList<ModelDocument> documents)
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

    public IndexReaderLifecycleModel Delete(string id) => this with
    {
        Working = Working.Remove(id),
        History = History.MarkChanged()
    };

    public IndexReaderLifecycleModel Update(ModelDocument replacement)
    {
        var working = Working.SetItem(replacement.Id, replacement);

        return this with
        {
            Working = working,
            History = History.ApplyLiveDeletes(working, replacement.Id).MarkChanged()
        };
    }

    public IndexReaderLifecycleModel Commit()
    {
        var history = History.ApplyLiveDeletes(Working);
        return this with { History = history.Append(Working) };
    }

    public IndexReaderLifecycleModel Refresh()
    {
        var latest = History.Latest;
        if (latest.Generation <= ManagerGeneration)
            return this;

        bool replacesReader = latest.ContentToken != ManagerContentToken;
        return this with
        {
            ManagerGeneration = latest.Generation,
            ManagerReaderVersion = replacesReader ? ManagerReaderVersion + 1 : ManagerReaderVersion,
            ManagerContentToken = latest.ContentToken,
            ManagerDocuments = replacesReader ? latest.Documents : ManagerDocuments
        };
    }

    public IndexReaderLifecycleModel Acquire(int leaseId) => this with
    {
        Leases = Leases.Add(leaseId, new ReaderLeaseModel(
            ManagerGeneration,
            ManagerReaderVersion,
            ManagerDocuments)),
        NextLeaseId = Math.Max(NextLeaseId, leaseId + 1)
    };

    public IndexReaderLifecycleModel Release(int leaseId) => this with
    {
        Leases = Leases.Remove(leaseId)
    };
}

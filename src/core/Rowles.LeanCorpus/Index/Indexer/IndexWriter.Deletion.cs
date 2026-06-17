using System.Diagnostics;
using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Index.Indexer;

public sealed partial class IndexWriter
{
    /// <summary>
    /// Queues a term-based deletion. Documents matching <paramref name="query"/> are deleted
    /// on the next <see cref="Commit"/> call.
    /// </summary>
    public void DeleteDocuments(TermQuery query)
    {
        using var activity = Diagnostics.LeanCorpusActivitySource.Source
            .StartActivity(Diagnostics.LeanCorpusActivitySource.DeleteQueue);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (WriteLock)
        {
            CommitState.PendingDeletes.Add((query.Field, query.Term, isSoftDelete: false));
            CommitState.ContentChangedSinceCommit = true;
        }
    }
}

using System.Diagnostics;
using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Index.Indexer;

public sealed partial class IndexWriter
{
    public void DeleteDocuments(TermQuery query)
    {
        using var activity = Diagnostics.LeanCorpusActivitySource.Source
            .StartActivity(Diagnostics.LeanCorpusActivitySource.DeleteQueue);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_writeLock)
        {
            _pendingDeletes.Add((query.Field, query.Term, isSoftDelete: false));
            _contentChangedSinceCommit = true;
        }
    }
}

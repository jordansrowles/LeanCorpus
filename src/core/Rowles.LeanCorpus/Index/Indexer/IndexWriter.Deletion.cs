using System.Diagnostics;
using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Index.Indexer;

public sealed partial class IndexWriter
{
    /// <summary>
    /// Queues a delete with deduplication: hard deletes always win over soft deletes
    /// for the same field+term.
    /// </summary>
    private void QueueDelete(string field, string term, bool isSoftDelete)
    {
        int ordinal = GetFieldOrdinal(field);
        byte[] termUtf8 = System.Text.Encoding.UTF8.GetBytes(term);
        byte[] prefixUtf8 = _deleteQueue.GetPrefix(ordinal)
            ?? System.Text.Encoding.UTF8.GetBytes(field + '\0');

        if (_deleteQueue.Queue(ordinal, termUtf8, prefixUtf8, isSoftDelete))
            _contentChangedSinceCommit = true;
    }

    public void DeleteDocuments(TermQuery query)
    {
        using var activity = Diagnostics.LeanCorpusActivitySource.Source
            .StartActivity(Diagnostics.LeanCorpusActivitySource.DeleteQueue);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_writeLock)
        {
            QueueDelete(query.Field, query.Term, isSoftDelete: false);
        }
    }
}

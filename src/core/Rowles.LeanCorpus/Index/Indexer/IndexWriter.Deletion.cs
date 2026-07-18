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
        byte[] prefixUtf8 = System.Text.Encoding.UTF8.GetBytes(field + '\0');

        // Check for duplicates
        for (int i = 0; i < _pendingDeletes.Count; i++)
        {
            var existing = _pendingDeletes[i];
            if (existing.FieldOrdinal == ordinal &&
                existing.TermUtf8.AsSpan().SequenceEqual(termUtf8))
            {
                if (!isSoftDelete && existing.IsSoftDelete)
                {
                    // Upgrade soft delete to hard delete
                    _pendingDeletes[i] = new DeleteTerm(ordinal, termUtf8, prefixUtf8, false);
                    _contentChangedSinceCommit = true;
                }
                // If hard delete already exists or both are soft, skip
                return;
            }
        }

        _pendingDeletes.Add(new DeleteTerm(ordinal, termUtf8, prefixUtf8, isSoftDelete));
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

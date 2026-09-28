using Rowles.LeanCorpus.Document;

namespace Rowles.LeanCorpus.Index.Indexer;

public sealed partial class IndexWriter
{
    /// <summary>
    /// Replaces documents with distinct terms as one ordered batch. Existing pending work and
    /// deletions are applied before the replacements are added, so commit-time deletes cannot
    /// remove documents from this batch.
    /// </summary>
    internal void UpdateDocuments(string field, IReadOnlyList<(string Term, LeanDocument Replacement)> updates)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0)
            return;

        EnterIndexingOperation();
        try
        {
            HashSet<string> terms = new(StringComparer.Ordinal);
            LeanDocument[] replacements = new LeanDocument[updates.Count];
            for (int i = 0; i < updates.Count; i++)
            {
                (string term, LeanDocument replacement) = updates[i];
                ArgumentNullException.ThrowIfNull(term);
                if (!terms.Add(term))
                    throw new ArgumentException("A replacement batch requires distinct delete terms.", nameof(updates));
                ValidateDocument(replacement);
                replacements[i] = replacement;
            }

            bool enteredCore = false;
            try
            {
                lock (_writeLock)
                {
                    // Complete earlier writes before applying this batch's deletions.
                    DwptManager.WaitForPendingFlushes(this);
                    DwptManager.FlushDwptPool(this);
                    DwptManager.WaitForPendingFlushes(this);
                    DwptManager.ValidateDocumentBatch(this, replacements);
                    ValidateVectorDimensions(replacements);

                    foreach ((string term, _) in updates)
                        QueueDelete(field, term, isSoftDelete: false);

                    // Apply all earlier and batched deletes against the pre-replacement state.
                    // Replacements are added only after the pending queue has been cleared.
                    DeletionApplier.ApplyPendingDeletions(
                        _deleteQueue, _committedSegments,
                        _directory, _commitGeneration, _config.DurableCommits, _config.Metrics,
                        _config.CodecCatalog);

                    enteredCore = true;
                    AddDocuments(replacements);
                }
            }
            catch
            {
                if (enteredCore)
                    MarkIndexingFailed();
                throw;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }
}

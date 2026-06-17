using Rowles.LeanCorpus.Document;

namespace Rowles.LeanCorpus.Index.Indexer;

public sealed partial class IndexWriter
{
    /// <summary>
    /// Initialises the DWPT pool for concurrent indexing.
    /// </summary>
    public void InitialiseDwptPool(int threadCount = 0)
    {
        DwptManager.InitialiseDwptPool(DwptState, Config, DefaultAnalyser, threadCount);
    }

    /// <summary>
    /// Lock-free document addition using per-thread DWPT buffers.
    /// </summary>
    public void AddDocumentLockFree(LeanDocument doc)
    {
        DwptManager.AddDocumentLockFree(DwptState, CommitState, Directory, Config, WriteLock, doc,
            EnterIndexingOperation, ExitIndexingOperation, ValidateDocument, MarkIndexingFailed);
    }

    /// <summary>
    /// Indexes a batch of documents using parallel per-thread writer buffers (DWPT).
    /// </summary>
    public void AddDocumentsConcurrent(IReadOnlyList<LeanDocument> documents)
    {
        DwptManager.AddDocumentsConcurrent(DwptState, CommitState, Directory, Config, WriteLock,
            DefaultAnalyser, documents,
            EnterIndexingOperation, ExitIndexingOperation, ValidateDocuments);
    }
}

using Rowles.LeanCorpus.Document;

namespace Rowles.LeanCorpus.Index.Indexer;

public sealed partial class IndexWriter
{
    [Obsolete("Configure IndexWriterConfig.IndexingConcurrency before constructing IndexWriter.")]
    public void InitialiseDwptPool(int threadCount = 0)
    {
        DwptManager.InitialiseDwptPool(this);
    }

    [Obsolete("Use AddDocument. LeanCorpus uses per-DWPT locking; this method has no separate lock-free path.")]
    public void AddDocumentLockFree(LeanDocument doc)
    {
        AddDocument(doc);
    }

    public void AddDocumentsConcurrent(IReadOnlyList<LeanDocument> documents)
    {
        DwptManager.AddDocumentsConcurrent(this, documents);
    }
}

using Rowles.LeanCorpus.Document;

namespace Rowles.LeanCorpus.Index.Indexer;

public sealed partial class IndexWriter
{
    public void InitialiseDwptPool(int threadCount = 0)
    {
        DwptManager.InitialiseDwptPool(this, threadCount);
    }

    public void AddDocumentLockFree(LeanDocument doc)
    {
        AddDocument(doc);
    }

    public void AddDocumentsConcurrent(IReadOnlyList<LeanDocument> documents)
    {
        AddDocuments(documents);
    }
}

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Mutable holder for DWPT pool state shared between <see cref="IndexWriter"/>
/// and <see cref="DwptManager"/>.
/// </summary>
internal sealed class DwptState
{
    public DocumentsWriterPerThread[]? DwptPool;
    public int DwptCounter;
}

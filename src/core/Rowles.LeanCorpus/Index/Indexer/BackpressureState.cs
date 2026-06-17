namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Mutable holder for backpressure state shared between <see cref="IndexWriter"/>
/// and <see cref="BackpressureController"/>.
/// </summary>
internal sealed class BackpressureState
{
    public SemaphoreSlim? BackpressureSemaphore;
    public int FlushElection;
    public int SemaphoreSlotsHeld;
}

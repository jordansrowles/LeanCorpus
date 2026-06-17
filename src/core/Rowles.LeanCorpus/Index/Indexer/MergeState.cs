namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Mutable holder for background merge state shared between <see cref="IndexWriter"/>
/// and <see cref="MergeScheduler"/>.
/// </summary>
internal sealed class MergeState
{
    public Task? MergeTask;
    public readonly CancellationTokenSource MergeCts = new();
    public readonly Lock MergeLock = new();
    public readonly Lock MergeIoLock = new();
}

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Mutable holder for snapshot state shared between <see cref="IndexWriter"/>
/// and <see cref="SnapshotManager"/>.
/// </summary>
internal sealed class SnapshotState
{
    public readonly List<IndexSnapshot> HeldSnapshots = [];
}

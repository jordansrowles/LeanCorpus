namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Optional collector extension that receives segment boundaries and the current scorer.
/// </summary>
public interface ILeafCollector : ICollector
{
    /// <summary>Starts collection for one segment.</summary>
    void SetSegment(int ordinal, int docBase, int maxDoc);

    /// <summary>Sets the scorer used while collecting the current segment.</summary>
    void SetScorer(Scorer scorer);
}

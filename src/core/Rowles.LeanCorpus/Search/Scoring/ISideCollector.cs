using Rowles.LeanCorpus.Index.Segment;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Optional side-channel invoked during a search pass for every scored document.
/// Implementations observe (docId, score, reader, localDocId) without affecting
/// the primary top-N collection. Used by aggregation, collapse, and facets to
/// avoid a second SearchAllMatches pass.
/// </summary>
internal interface ISideCollector
{
    /// <summary>Receives a scored document during the search pass.</summary>
    void Collect(int globalDocId, float score, SegmentReader reader, int localDocId);
}

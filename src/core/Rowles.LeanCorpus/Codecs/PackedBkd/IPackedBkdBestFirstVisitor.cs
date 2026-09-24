namespace Rowles.LeanCorpus.Codecs.PackedBkd;

/// <summary>Receives packed BKD leaves in nondecreasing lower-bound order.</summary>
internal interface IPackedBkdBestFirstVisitor
{
    double GetLowerBoundDistance(uint minimumX, uint maximumX, uint minimumY, uint maximumY);
    double WorstCandidateDistance { get; }
    bool HasFullCandidateSet { get; }
    bool ShouldStop { get; }
    int CurrentCandidateCount { get; }
    int PeakCandidateCount { get; }
    long ExactDistanceCalculations { get; }
    long FilterCandidatesRejected { get; }
    long CandidateUpdates { get; }
    void Visit(int documentId, ReadOnlySpan<byte> packedValue);
}

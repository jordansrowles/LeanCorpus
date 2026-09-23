namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

internal struct PackedBkdTraversalStats
{
    internal long CellsVisited;
    internal long CellsPruned;
    internal long LeavesVisited;
    internal long LeavesSemanticallyValidated;
    internal long PackedValuesDecoded;
    internal long DocumentsVisited;
    internal int PeakLeafScratch;
}

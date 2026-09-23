namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

/// <summary>Accounts for managed memory owned by one Packed BKD build.</summary>
internal sealed class PackedBkdBuildMemoryTracker
{
    internal PackedBkdBuildMemoryTracker(long budgetBytes)
    {
        if (budgetBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(budgetBytes));
        BudgetBytes = budgetBytes;
    }

    internal long BudgetBytes { get; }

    internal long CurrentBytes { get; private set; }

    internal long PeakBytes { get; private set; }

    internal bool TryReserve(long actualBytes)
    {
        if (actualBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(actualBytes));
        long next = checked(CurrentBytes + actualBytes);
        if (next > BudgetBytes)
            return false;
        CurrentBytes = next;
        PeakBytes = Math.Max(PeakBytes, next);
        return true;
    }

    internal void Reserve(long actualBytes)
    {
        if (!TryReserve(actualBytes))
            throw new OutOfMemoryException($"The Packed BKD build exceeded its {BudgetBytes} byte memory budget.");
    }

    internal void Release(long actualBytes)
    {
        if (actualBytes < 0 || actualBytes > CurrentBytes)
            throw new ArgumentOutOfRangeException(nameof(actualBytes));
        CurrentBytes -= actualBytes;
    }
}

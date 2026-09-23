namespace Rowles.LeanCorpus.Codecs.PackedBkd;

/// <summary>Build limits and cancellation options for a packed BKD flush.</summary>
internal readonly record struct PackedBkdBuildOptions(
    long MemoryBudgetBytes,
    string? SpillDirectory = null,
    bool ForceSpill = false,
    CancellationToken CancellationToken = default)
{
    internal static PackedBkdBuildOptions Default { get; } = new(16L * 1024 * 1024);

    internal void Validate()
    {
        if (MemoryBudgetBytes < 1024)
            throw new ArgumentOutOfRangeException(nameof(MemoryBudgetBytes), MemoryBudgetBytes, "The packed BKD build budget must be at least 1024 bytes.");
        if (SpillDirectory is not null && string.IsNullOrWhiteSpace(SpillDirectory))
            throw new ArgumentException("A supplied spill directory must not be empty.", nameof(SpillDirectory));
    }
}

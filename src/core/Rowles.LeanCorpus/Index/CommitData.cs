namespace Rowles.LeanCorpus.Index;

internal sealed class CommitData
{
    public List<string> Segments { get; set; } = [];

    public int Generation { get; set; }

    public long ContentToken { get; set; }

    /// <summary>
    /// Validates invariants after deserialisation. Throws <see cref="InvalidDataException"/>
    /// when required fields are missing, empty, or out of range.
    /// </summary>
    internal void Validate()
    {
        if (Segments is null)
            throw new InvalidDataException("Commit data has a null Segments list.");
        if (Generation < 0)
            throw new InvalidDataException($"Commit data has a negative Generation ({Generation}).");
    }
}

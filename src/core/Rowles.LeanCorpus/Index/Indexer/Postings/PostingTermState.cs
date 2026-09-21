namespace Rowles.LeanCorpus.Index.Indexer.Postings;

[Flags]
internal enum PostingFlags : byte
{
    None = 0,
    HasFreqs = 1,
    HasPositions = 2,
    HasPayloads = 4,
    HasOffsets = 8,
}

/// <summary>
/// The compact mutable state for one qualified term in a postings store.
/// Keep this type reference-free so its array can be rented without clearing.
/// </summary>
internal struct PostingTermState
{
    internal int FieldOrdinal;
    internal PostingFlags Flags;

    internal int CurrentDocId;
    internal int LastFlushedDocId;
    internal int CurrentFreq;
    internal int CurrentPositionCount;
    internal int DocFreq;

    internal int LastPosition;

    internal PostingsByteArena.StreamCursor DocStreamCursor;
    internal PostingsByteArena.StreamCursor ProxStreamCursor;

    internal void Initialise(int fieldOrdinal)
    {
        FieldOrdinal = fieldOrdinal;
        Flags = PostingFlags.None;
        CurrentDocId = -1;
        LastFlushedDocId = -1;
        CurrentFreq = 0;
        CurrentPositionCount = 0;
        DocFreq = 0;
        LastPosition = 0;
        DocStreamCursor = default;
        ProxStreamCursor = default;
    }
}

namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>
/// Controls which postings data is written to the inverted index for a field.
/// Lower levels save disk space and indexing time; higher levels enable
/// phrase queries, highlighting, and scored term frequency.
/// </summary>
public enum FieldIndexOptions
{
    /// <summary>
    /// Only doc IDs are indexed. Suitable for filter-only fields that never
    /// participate in scoring or phrase queries. Smallest on-disk footprint.
    /// </summary>
    DocsOnly = 0,

    /// <summary>
    /// Doc IDs and term frequencies are indexed. Enables scoring (BM25, TF-IDF)
    /// but not phrase or span queries. Good for relevance-ranked full-text where
    /// positional queries are not required.
    /// </summary>
    DocsAndFreqs = 1,

    /// <summary>
    /// Doc IDs, term frequencies, and positions are indexed. Enables phrase
    /// queries, span queries, and highlighting. Default for <see cref="TextField"/>.
    /// </summary>
    DocsAndFreqsAndPositions = 2,

    /// <summary>
    /// Full indexing including character offsets. Required for the unified
    /// highlighter and fast-vector highlighting. Largest on-disk footprint.
    /// </summary>
    DocsAndFreqsAndPositionsAndOffsets = 3,
}

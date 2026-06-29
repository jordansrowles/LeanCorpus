using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Postings-related methods for SegmentReader.
/// </summary>
public sealed partial class SegmentReader
{
    /// <summary>
    /// Returns a PostingsEnum cursor for the given qualified term (field\0term).
    /// Decodes the postings list once; caller must dispose.
    /// </summary>
    public PostingsEnum GetPostingsEnum(string qualifiedTerm)
    {
        if (!TryGetCachedOffset(qualifiedTerm, out long offset))
            return PostingsEnum.Empty;

        return PostingsEnum.Create(_posInput, offset);
    }

    /// <summary>
    /// Returns a PostingsEnum at a known postings offset, skipping the dictionary lookup.
    /// Use when the offset was already obtained from a term scan (e.g. prefix/wildcard).
    /// </summary>
    public PostingsEnum GetPostingsEnumAtOffset(long offset)
        => PostingsEnum.Create(_posInput, offset);

    /// <summary>
    /// Returns a PostingsEnum with decoded positions for phrase queries.
    /// </summary>
    public PostingsEnum GetPostingsEnumWithPositions(string qualifiedTerm)
    {
        if (!TryGetCachedOffset(qualifiedTerm, out long offset))
            return PostingsEnum.Empty;

        return PostingsEnum.CreateWithPositions(_posInput, offset);
    }

    /// <summary>Returns positional data for a term in a specific document, or null if unavailable.</summary>
    public int[]? GetPositions(string field, string term, int docId)
    {
        var qualifiedTerm = GetQualifiedTerm(field, term);
        if (!_dicReader.TryGetPostingsOffset(qualifiedTerm, out long offset))
            return null;

        return ReadPositionsAtOffset(offset, docId);
    }

    /// <summary>Returns positional data for a pre-built qualified term string.</summary>
    internal ReadOnlySpan<int> GetPositions(string qualifiedTerm, int docId)
    {
        var positions = GetPositionsArray(qualifiedTerm, docId);
        return positions is null ? ReadOnlySpan<int>.Empty : positions.AsSpan();
    }

    /// <summary>Returns positional data for a pre-built qualified term string.</summary>
    internal int[]? GetPositionsArray(string qualifiedTerm, int docId)
    {
        if (!TryGetCachedOffset(qualifiedTerm, out long offset))
            return null;

        return ReadPositionsAtOffset(offset, docId);
    }

    /// <summary>
    /// Returns the term frequency for a given term in a specific document.
    /// </summary>
    public int GetTermFrequency(string field, string term, int docId)
    {
        var qualifiedTerm = GetQualifiedTerm(field, term);
        if (!_dicReader.TryGetPostingsOffset(qualifiedTerm, out long offset))
            return 0;

        return ReadTermFrequency(offset, docId);
    }

    /// <summary>
    /// Returns the term frequency for a pre-built qualified term string.
    /// </summary>
    internal int GetTermFrequency(string qualifiedTerm, int docId)
    {
        if (!_dicReader.TryGetPostingsOffset(qualifiedTerm, out long offset))
            return 0;

        return ReadTermFrequency(offset, docId);
    }

    private int[] ReadPostingsAtOffset(long offset)
    {
        using var pe = PostingsEnum.Create(_posInput, offset);
        var ids = new int[pe.DocFreq];
        int i = 0;
        while (pe.MoveNext()) ids[i++] = pe.DocId;
        return ids;
    }

    private int ReadTermFrequency(long offset, int targetDocId)
    {
        using var pe = PostingsEnum.Create(_posInput, offset);
        while (pe.MoveNext())
        {
            if (pe.DocId == targetDocId) return pe.Freq;
            if (pe.DocId > targetDocId) return 0;
        }
        return 0;
    }

    private int[]? ReadPositionsAtOffset(long offset, int docId)
    {
        using var pe = PostingsEnum.CreateWithPositions(_posInput, offset);
        while (pe.MoveNext())
        {
            if (pe.DocId == docId)
                return pe.GetCurrentPositions().ToArray();
            if (pe.DocId > docId)
                return null;
        }
        return null;
    }

}

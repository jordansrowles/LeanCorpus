using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Index.Segment;

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

        return PostingsEnum.Create(_posInput, offset, _postingsVersion);
    }

    /// <summary>
    /// Returns a PostingsEnum at a known postings offset, skipping the dictionary lookup.
    /// Use when the offset was already obtained from a term scan (e.g. prefix/wildcard).
    /// </summary>
    public PostingsEnum GetPostingsEnumAtOffset(long offset)
        => PostingsEnum.Create(_posInput, offset, _postingsVersion);

    /// <summary>
    /// Returns a PostingsEnum with decoded positions for phrase queries.
    /// </summary>
    public PostingsEnum GetPostingsEnumWithPositions(string qualifiedTerm)
    {
        if (!TryGetCachedOffset(qualifiedTerm, out long offset))
            return PostingsEnum.Empty;

        return PostingsEnum.CreateWithPositions(_posInput, offset, _postingsVersion);
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
        if (!_dicReader.TryGetPostingsOffset(qualifiedTerm, out long offset))
            return ReadOnlySpan<int>.Empty;

        var positions = ReadPositionsAtOffset(offset, docId);
        return positions.AsSpan();
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
        if (_postingsVersion >= 3)
        {
            using var pe = PostingsEnum.Create(_posInput, offset, _postingsVersion);
            var ids = new int[pe.DocFreq];
            int i = 0;
            while (pe.MoveNext()) ids[i++] = pe.DocId;
            return ids;
        }

        long cursor = offset;
        int count = _posInput.ReadInt32(ref cursor);
        // Skip past skip entries
        int skipCount = _posInput.ReadInt32(ref cursor);
        if (skipCount > 0)
            cursor += skipCount * 8L;
        var docIds = new int[count];
        int prev = 0;
        for (int i = 0; i < count; i++)
        {
            prev = ReadNextDocId(_posInput, prev, ref cursor);
            docIds[i] = prev;
        }
        return docIds;
    }

    private int ReadTermFrequency(long offset, int targetDocId)
    {
        if (_postingsVersion >= 3)
        {
            using var pe = PostingsEnum.Create(_posInput, offset, _postingsVersion);
            while (pe.MoveNext())
            {
                if (pe.DocId == targetDocId) return pe.Freq;
                if (pe.DocId > targetDocId) return 0;
            }
            return 0;
        }

        long cursor = offset;
        int count = _posInput.ReadInt32(ref cursor);
        // Skip past skip entries
        int skipCount = _posInput.ReadInt32(ref cursor);
        if (skipCount > 0)
            cursor += skipCount * 8L;

        var ids = new int[count];
        int prev = 0;
        for (int i = 0; i < count; i++)
        {
            prev = ReadNextDocId(_posInput, prev, ref cursor);
            ids[i] = prev;
        }

        bool hasFreqs = _posInput.ReadBoolean(ref cursor);
        if (!hasFreqs) return 1;

        for (int i = 0; i < count; i++)
        {
            int freq = _posInput.ReadVarInt(ref cursor);
            if (ids[i] == targetDocId)
                return freq;
        }

        return 0;
    }

    private int[]? ReadPositionsAtOffset(long offset, int docId)
    {
        if (_postingsVersion >= 3)
        {
            using var pe = PostingsEnum.CreateWithPositions(_posInput, offset, _postingsVersion);
            while (pe.MoveNext())
            {
                if (pe.DocId == docId)
                    return pe.GetCurrentPositions().ToArray();
                if (pe.DocId > docId)
                    return null;
            }
            return null;
        }

        long cursor = offset;
        int count = _posInput.ReadInt32(ref cursor);
        // Skip past skip entries
        int skipCount = _posInput.ReadInt32(ref cursor);
        if (skipCount > 0)
            cursor += skipCount * 8L;

        // Stream through doc IDs to find target index (zero alloc)
        int targetIndex = -1;
        int prev = 0;
        for (int i = 0; i < count; i++)
        {
            prev = ReadNextDocId(_posInput, prev, ref cursor);
            if (prev == docId && targetIndex < 0)
                targetIndex = i;
        }
        if (targetIndex < 0) return null;

        bool hasFreqs = _posInput.ReadBoolean(ref cursor);
        if (hasFreqs)
        {
            for (int i = 0; i < count; i++)
                _posInput.ReadVarInt(ref cursor); // skip freqs
        }

        bool hasPositions = _posInput.ReadBoolean(ref cursor);

        bool hasPayloads = false;
        if (_postingsVersion >= 2)
            hasPayloads = _posInput.ReadBoolean(ref cursor);

        if (!hasPositions) return null;

        for (int i = 0; i < targetIndex; i++)
        {
            int posCount = _posInput.ReadVarInt(ref cursor);
            for (int j = 0; j < posCount; j++)
            {
                _posInput.ReadVarInt(ref cursor); // position delta
                if (hasPayloads)
                {
                    int payloadLen = _posInput.ReadVarInt(ref cursor);
                    if (payloadLen > 0)
                        cursor += payloadLen;
                }
            }
        }

        int targetPosCount = _posInput.ReadVarInt(ref cursor);
        var positions = new int[targetPosCount];
        int prevPos = 0;
        for (int i = 0; i < targetPosCount; i++)
        {
            prevPos += _posInput.ReadVarInt(ref cursor);
            positions[i] = prevPos;
            if (hasPayloads)
            {
                int payloadLen = _posInput.ReadVarInt(ref cursor);
                if (payloadLen > 0)
                    cursor += payloadLen;
            }
        }

        return positions;
    }

    private static int ReadNextDocId(IndexInput input, int previous, ref long position)
    {
        int delta = input.ReadVarInt(ref position);
        if (delta < 0)
            throw new InvalidDataException("Postings data is corrupt: negative delta encountered.");

        try
        {
            return checked(previous + delta);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException("Postings data is corrupt: doc ID delta overflow.", ex);
        }
    }
}

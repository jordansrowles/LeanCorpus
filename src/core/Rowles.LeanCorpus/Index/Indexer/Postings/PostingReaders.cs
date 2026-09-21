using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Indexer.Postings;

internal readonly struct PostingDoc
{
    internal PostingDoc(int docId, int freq, int positionCount)
    {
        DocId = docId;
        Freq = freq;
        PositionCount = positionCount;
    }

    internal int DocId { get; }
    internal int Freq { get; }
    internal int PositionCount { get; }
}

internal readonly struct PostingPositionHeader
{
    internal PostingPositionHeader(int position, int payloadLength, bool hasOffsets, int startOffset, int endOffset)
    {
        Position = position;
        PayloadLength = payloadLength;
        HasOffsets = hasOffsets;
        StartOffset = startOffset;
        EndOffset = endOffset;
    }

    internal int Position { get; }
    internal int PayloadLength { get; }
    internal bool HasOffsets { get; }
    internal int StartOffset { get; }
    internal int EndOffset { get; }
}

/// <summary>Reads document records and the pending final document for one term.</summary>
internal ref struct PostingDocReader
{
    private readonly PostingTermState _state;
    private PostingsByteArena.Reader _reader;
    private int _lastDocId;
    private int _returnedCount;
    private bool _hasLastDoc;
    private bool _pendingFinalReturned;

    internal PostingDocReader(PostingsByteArena arena, in PostingTermState state)
    {
        _state = state;
        _reader = arena.OpenReader(state.DocStreamCursor);
        _lastDocId = 0;
        _returnedCount = 0;
        _hasLastDoc = false;
        _pendingFinalReturned = false;
    }

    internal bool MoveNext(out PostingDoc posting)
    {
        if (!_reader.EndOfStream)
        {
            int delta = ReadInt32(_reader.ReadVarUInt(), "document delta");
            int freq = ReadInt32(_reader.ReadVarUInt(), "frequency");
            int positionCount = ReadInt32(_reader.ReadVarUInt(), "position count");
            int docId;
            try
            {
                docId = checked(_hasLastDoc ? _lastDocId + delta : delta);
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException("A postings document ID overflowed.", ex);
            }

            if (docId < 0 || (_hasLastDoc && docId < _lastDocId))
                throw new InvalidDataException("A postings document stream is not monotonic.");

            _lastDocId = docId;
            _hasLastDoc = true;
            _returnedCount++;
            posting = new PostingDoc(docId, freq, positionCount);
            return true;
        }

        if (!_pendingFinalReturned && _state.CurrentDocId >= 0 && _state.CurrentDocId > _state.LastFlushedDocId)
        {
            if (_hasLastDoc && _state.CurrentDocId < _lastDocId)
                throw new InvalidDataException("The pending postings document is not monotonic.");

            _pendingFinalReturned = true;
            _returnedCount++;
            posting = new PostingDoc(_state.CurrentDocId, _state.CurrentFreq, _state.CurrentPositionCount);
            return true;
        }

        if (_returnedCount != _state.DocFreq)
            throw new InvalidDataException("The postings document frequency does not match its encoded records.");

        posting = default;
        return false;
    }

    private static int ReadInt32(uint value, string name)
    {
        if (value > int.MaxValue)
            throw new InvalidDataException($"The postings {name} exceeds Int32.");
        return (int)value;
    }
}

/// <summary>Reads position headers and leaves each raw payload pending for the caller.</summary>
internal ref struct PostingProxReader
{
    private readonly bool _storePayloads;
    private readonly bool _storeTermVectors;
    private PostingsByteArena.Reader _reader;
    private int _lastPosition;
    private int _pendingPayloadLength;
    private bool _payloadPending;

    internal PostingProxReader(
        PostingsByteArena arena,
        in PostingTermState state,
        bool storePayloads,
        bool storeTermVectors)
    {
        _storePayloads = storePayloads;
        _storeTermVectors = storeTermVectors;
        _reader = arena.OpenReader(state.ProxStreamCursor);
        _lastPosition = 0;
        _pendingPayloadLength = 0;
        _payloadPending = false;
    }

    internal void StartDocument()
    {
        if (_payloadPending)
            throw new InvalidOperationException("The current postings payload must be consumed before starting another document.");
        _lastPosition = 0;
    }

    internal bool ReadNext(out PostingPositionHeader position)
    {
        if (_payloadPending)
            throw new InvalidOperationException("The current postings payload must be consumed before reading another position.");
        if (_reader.EndOfStream)
        {
            position = default;
            return false;
        }

        int positionDelta = ReadInt32(_reader.ReadVarUInt(), "position delta");
        int absolutePosition;
        try
        {
            absolutePosition = checked(_lastPosition + positionDelta);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException("A postings position overflowed.", ex);
        }

        int payloadLength = _storePayloads
            ? ReadInt32(_reader.ReadVarUInt(), "payload length")
            : 0;
        bool hasOffsets = false;
        int startOffset = 0;
        int endOffset = 0;
        if (_storeTermVectors)
        {
            uint offsetMarker = _reader.ReadVarUInt();
            if (offsetMarker > 1)
                throw new InvalidDataException("A postings offset marker must be zero or one.");
            hasOffsets = offsetMarker == 1;
            if (hasOffsets)
            {
                startOffset = ReadInt32(_reader.ReadVarUInt(), "start offset");
                endOffset = ReadInt32(_reader.ReadVarUInt(), "end offset");
                if (endOffset < startOffset)
                    throw new InvalidDataException("A postings end offset precedes its start offset.");
            }
        }

        _lastPosition = absolutePosition;
        _pendingPayloadLength = payloadLength;
        _payloadPending = payloadLength != 0;
        position = new PostingPositionHeader(absolutePosition, payloadLength, hasOffsets, startOffset, endOffset);
        return true;
    }

    internal void CopyPayloadTo(Span<byte> destination)
    {
        if (destination.Length < _pendingPayloadLength)
            throw new ArgumentException("The destination is smaller than the pending postings payload.", nameof(destination));
        if (_pendingPayloadLength != 0)
            _reader.CopyBytes(destination[.._pendingPayloadLength]);
        _pendingPayloadLength = 0;
        _payloadPending = false;
    }

    internal void CopyPayloadTo(IndexOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (_pendingPayloadLength != 0)
            _reader.CopyBytes(output, _pendingPayloadLength);
        _pendingPayloadLength = 0;
        _payloadPending = false;
    }

    internal void CopyPayloadTo(ISequentialIndexOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (_pendingPayloadLength > 0)
            _reader.CopyBytes(output, _pendingPayloadLength);
        _pendingPayloadLength = 0;
        _payloadPending = false;
    }

    internal void SkipPayload()
    {
        if (_pendingPayloadLength != 0)
            _reader.SkipBytes(_pendingPayloadLength);
        _pendingPayloadLength = 0;
        _payloadPending = false;
    }

    private static int ReadInt32(uint value, string name)
    {
        if (value > int.MaxValue)
            throw new InvalidDataException($"The postings {name} exceeds Int32.");
        return (int)value;
    }
}

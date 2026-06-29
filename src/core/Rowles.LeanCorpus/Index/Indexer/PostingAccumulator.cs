using System.Buffers;
using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Accumulates doc IDs, term frequencies, and positions for a single qualified term
/// during indexing. Uses ArrayPool-backed buffers to avoid GC pressure from repeated
/// resize-copy-abandon cycles.
/// </summary>
/// <remarks>
/// Positions are stored as VarInt delta-encoded bytes. The first position per posting is
/// stored as an absolute VarInt; subsequent positions are VarInt deltas from the first.
/// Call <see cref="ReturnBuffers"/> after flush to return rented arrays.
/// </remarks>
internal sealed class PostingAccumulator
{
    private static readonly ArrayPool<int> IntPool = ArrayPool<int>.Shared;
    private static readonly ArrayPool<byte> BytePool = ArrayPool<byte>.Shared;
    private int[] _docIds;           // first entry: absolute base; rest: deltas from previous
    private int[] _freqs;
    private int[] _posStarts;        // byte offset into _posBuf per posting
    private int[] _posByteLens;      // byte length of encoded region per posting
    private byte[] _posBuf;
    private int _posBufUsed;         // bytes used in _posBuf
    private int _posBufLen;          // _posBuf array length
    private byte[]?[][]? _payloads;
    private int[][]? _startOffsets;  // per-posting arrays of start offsets, aligned to positions
    private int[][]? _endOffsets;    // per-posting arrays of end offsets, aligned to positions
    private int _count;
    private int _docIdsLen;          // logical length (may be < rented array length)
    private long _cachedEstimatedBytes;
    private bool _hasFreqs;
    private bool _hasPositions;
    private bool _hasOffsets;

    private int _lastAbsoluteDocId;  // last absolute doc ID for delta computation
    private int[]? _absoluteCache;   // lazily expanded absolute doc IDs
    private bool _absoluteCacheValid;

    private const int NoPositionSentinel = -1;

    public PostingAccumulator()
    {
        _docIds = IntPool.Rent(4);
        _freqs = IntPool.Rent(4);
        _posStarts = IntPool.Rent(4);
        _posByteLens = IntPool.Rent(4);

        _posBuf = BytePool.Rent(16);
        _docIdsLen = 4;
        _posBufLen = 16;
        _posBufUsed = 0;
        _count = 0;
        _lastAbsoluteDocId = -1;
        _cachedEstimatedBytes = RecomputeEstimatedBytes();
    }

    public int Count => _count;

    /// <summary>First absolute doc ID (the base). Only valid when Count > 0.</summary>
    public int FirstDocId => _count > 0 ? _docIds[0] : -1;

    public long EstimatedBytes => _cachedEstimatedBytes;

    private long RecomputeEstimatedBytes()
    {
        const long ObjectOverhead = 64;
        long bufferBytes = (long)(_docIds.Length + _freqs.Length + _posStarts.Length +
            _posByteLens.Length) * sizeof(int)
            + _posBuf.Length;
        if (_absoluteCache is not null)
            bufferBytes += _absoluteCache.Length * sizeof(int);
        return ObjectOverhead + bufferBytes;
    }

    // ─────── Absolute doc ID expansion ───────

    /// <summary>
    /// Returns absolute doc IDs, expanding from deltas on first call.
    /// The returned span references an internal cache that is invalidated on mutation.
    /// </summary>
    public ReadOnlySpan<int> DocIds
    {
        get
        {
            if (!_absoluteCacheValid || _absoluteCache is null)
                ExpandToAbsolute();
            return _absoluteCache.AsSpan(0, _count);
        }
    }

    /// <summary>
    /// Returns the raw delta-encoded doc IDs: first entry is absolute base,
    /// subsequent entries are deltas from the previous doc ID.
    /// </summary>
    public ReadOnlySpan<int> DocIdDeltas => _docIds.AsSpan(0, _count);

    private void ExpandToAbsolute()
    {
        if (_absoluteCache is null || _absoluteCache.Length < _docIdsLen)
        {
            if (_absoluteCache is not null) IntPool.Return(_absoluteCache, clearArray: false);
            _absoluteCache = IntPool.Rent(_docIdsLen);
        }

        int prev = 0;
        for (int i = 0; i < _count; i++)
        {
            prev += _docIds[i];
            _absoluteCache[i] = prev;
        }
        _absoluteCacheValid = true;
        _cachedEstimatedBytes = RecomputeEstimatedBytes();
    }

    private void InvalidateAbsoluteCache()
    {
        _absoluteCacheValid = false;
    }

    // ─────── VarInt helpers ───────

    /// <summary>Decodes the first absolute position from the posting's encoded bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetFirstPosition(int index)
    {
        int start = _posStarts[index];
        if (start == NoPositionSentinel) return 0;
        ReadVarInt(_posBuf.AsSpan(start), out int first);
        return first;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int VarIntEncodedSize(int value)
    {
        uint v = (uint)value;
        if (v < 0x80) return 1;
        if (v < 0x4000) return 2;
        if (v < 0x200000) return 3;
        if (v < 0x10000000) return 4;
        return 5;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int WriteVarInt(Span<byte> dest, int value)
    {
        uint v = (uint)value;
        int i = 0;
        while (v >= 0x80) { dest[i++] = (byte)(v | 0x80); v >>= 7; }
        dest[i++] = (byte)v;
        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ReadVarInt(ReadOnlySpan<byte> src, out int value)
    {
        uint v = 0; int shift = 0; int i = 0; byte b;
        do { b = src[i++]; v |= (uint)(b & 0x7F) << shift; shift += 7; } while ((b & 0x80) != 0);
        value = (int)v;
        return i;
    }

    // ─────── Internal write helpers ───────

    private void EnsurePosBufCapacity(int requiredBytes)
    {
        if (requiredBytes <= _posBufLen) return;
        int newLen = Math.Max(_posBufLen * 2, requiredBytes);
        var newBuf = BytePool.Rent(newLen);
        if (_posBufUsed > 0) Array.Copy(_posBuf, newBuf, _posBufUsed);
        BytePool.Return(_posBuf, clearArray: false);
        _posBuf = newBuf;
        _posBufLen = newLen;
        _cachedEstimatedBytes = RecomputeEstimatedBytes();
    }

    /// <summary>Writes a single delta VarInt for the last posting, relocating if needed.</summary>
    private void AppendDeltaToLastPosting(int delta)
    {
        int idx = _count - 1;
        int byteLen = _posByteLens[idx];
        int encodedSize = VarIntEncodedSize(delta);

        if (_posStarts[idx] + byteLen + encodedSize <= _posBufLen && _posStarts[idx] + byteLen >= _posBufUsed - byteLen)
        {
            WriteVarInt(_posBuf.AsSpan(_posStarts[idx] + byteLen), delta);
            _posByteLens[idx] = byteLen + encodedSize;
            if (_posStarts[idx] + byteLen == _posBufUsed)
                _posBufUsed += encodedSize;
        }
        else
        {
            EnsurePosBufCapacity(_posBufUsed + byteLen + encodedSize);
            Array.Copy(_posBuf, _posStarts[idx], _posBuf, _posBufUsed, byteLen);
            _posStarts[idx] = _posBufUsed;
            _posBufUsed += byteLen;
            _posBufUsed += WriteVarInt(_posBuf.AsSpan(_posBufUsed), delta);
            _posByteLens[idx] = byteLen + encodedSize;
        }
    }

    private void AppendDeltasToLastPosting(ReadOnlySpan<int> deltas)
    {
        int idx = _count - 1;
        int byteLen = _posByteLens[idx];
        int extraBytes = 0;
        for (int p = 0; p < deltas.Length; p++)
            extraBytes += VarIntEncodedSize(deltas[p]);

        EnsurePosBufCapacity(_posBufUsed + byteLen + extraBytes);
        Array.Copy(_posBuf, _posStarts[idx], _posBuf, _posBufUsed, byteLen);
        _posStarts[idx] = _posBufUsed;
        _posBufUsed += byteLen;
        for (int p = 0; p < deltas.Length; p++)
            _posBufUsed += WriteVarInt(_posBuf.AsSpan(_posBufUsed), deltas[p]);
        _posByteLens[idx] = byteLen + extraBytes;
    }

    /// <summary>Appends a new posting entry with delta doc ID storage.</summary>
    private void AddNewPosting(int docId, ReadOnlySpan<int> positions)
    {
        if (_count == _docIdsLen) Grow();

        int delta;
        if (_count == 0)
        {
            delta = docId;           // first entry: absolute base
        }
        else
        {
            delta = docId - _lastAbsoluteDocId;
        }

        int firstPos = positions[0];
        int totalBytes = VarIntEncodedSize(firstPos);
        for (int p = 1; p < positions.Length; p++)
            totalBytes += VarIntEncodedSize(positions[p] - firstPos);

        EnsurePosBufCapacity(_posBufUsed + totalBytes);

        _docIds[_count] = delta;
        _freqs[_count] = positions.Length;
        _posStarts[_count] = _posBufUsed;

        _posBufUsed += WriteVarInt(_posBuf.AsSpan(_posBufUsed), firstPos);
        for (int p = 1; p < positions.Length; p++)
            _posBufUsed += WriteVarInt(_posBuf.AsSpan(_posBufUsed), positions[p] - firstPos);

        _posByteLens[_count] = totalBytes;
        _lastAbsoluteDocId = docId;
        _count++;
        InvalidateAbsoluteCache();
    }

    /// <summary>
    /// Checks whether the last posting matches the given document, using either the
    /// absolute cache or the tracked last-absolute value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsLastDoc(int docId)
    {
        return _count > 0 && _lastAbsoluteDocId == docId;
    }

    // ─────── Raw merge helpers ───────

    /// <summary>
    /// Returns the VarInt-encoded delta bytes (after the first position), the decoded first
    /// absolute position, and the frequency. Zero-allocation.
    /// </summary>
    internal void GetEncodedPositionDeltas(int index, out ReadOnlySpan<byte> deltas, out int firstPosition, out int freq)
    {
        int start = _posStarts[index];
        if (start == NoPositionSentinel || _freqs[index] == 0)
        {
            deltas = ReadOnlySpan<byte>.Empty;
            firstPosition = 0;
            freq = 0;
            return;
        }

        var src = _posBuf.AsSpan(start, _posByteLens[index]);
        ReadVarInt(src, out firstPosition);
        deltas = src.Slice(VarIntEncodedSize(firstPosition));
        freq = _freqs[index];
    }

    /// <summary>
    /// Appends positions for a new document from raw VarInt-encoded delta bytes.
    /// Used by the merge path (DWPT merge and segment merge).
    /// </summary>
    internal void AddEncodedPositions(int docId, int firstPosition, ReadOnlySpan<byte> deltaBytes, int freq)
    {
        _hasFreqs = true;
        _hasPositions = true;

        int firstBytes = VarIntEncodedSize(firstPosition);
        int totalBytes = firstBytes + deltaBytes.Length;

        if (IsLastDoc(docId))
        {
            AppendDeltasToLastPostingRaw(deltaBytes, firstPosition);
            _freqs[_count - 1] += freq;
            return;
        }

        if (_count == _docIdsLen) Grow();
        EnsurePosBufCapacity(_posBufUsed + totalBytes);

        int delta = _count == 0 ? docId : docId - _lastAbsoluteDocId;
        _docIds[_count] = delta;
        _freqs[_count] = freq;
        _posStarts[_count] = _posBufUsed;

        _posBufUsed += WriteVarInt(_posBuf.AsSpan(_posBufUsed), firstPosition);
        deltaBytes.CopyTo(_posBuf.AsSpan(_posBufUsed));
        _posBufUsed += deltaBytes.Length;
        _posByteLens[_count] = totalBytes;
        _lastAbsoluteDocId = docId;
        _count++;
        InvalidateAbsoluteCache();
    }

    /// <summary>Appends raw VarInt deltas from firstPos to the last posting without re-encoding.</summary>
    private void AppendDeltasToLastPostingRaw(ReadOnlySpan<byte> deltaBytes, int firstPosition)
    {
        int idx = _count - 1;
        int byteLen = _posByteLens[idx];
        EnsurePosBufCapacity(_posBufUsed + deltaBytes.Length);

        int existingFirst = GetFirstPosition(idx);
        if (existingFirst != firstPosition)
        {
            int newBase = existingFirst;
            int offset = 0;
            while (offset < deltaBytes.Length)
            {
                offset += ReadVarInt(deltaBytes.Slice(offset), out int delta);
                int abs = firstPosition + delta;
                int newDelta = abs - newBase;
                _posBufUsed += WriteVarInt(_posBuf.AsSpan(_posBufUsed), newDelta);
            }
            _posByteLens[idx] = _posBufUsed - _posStarts[idx];
        }
        else
        {
            deltaBytes.CopyTo(_posBuf.AsSpan(_posBufUsed));
            _posBufUsed += deltaBytes.Length;
            _posByteLens[idx] = byteLen + deltaBytes.Length;
        }
    }

    // ─────── Public API ───────

    /// <summary>
    /// Adds a posting with the field's index options controlling which data is stored.
    /// This overload is the primary entry point for the indexing hot path.
    /// </summary>
    public void Add(int docId, int position, FieldIndexOptions indexOptions, int startOffset = 0, int endOffset = 0)
    {
        if ((int)indexOptions <= (int)FieldIndexOptions.DocsOnly)
        {
            // DocsOnly: skip frequency and position data entirely — only track the doc ID.
            AddDocOnly(docId);
            return;
        }

        _hasFreqs = true;
        if ((int)indexOptions >= (int)FieldIndexOptions.DocsAndFreqsAndPositions)
            _hasPositions = true;

        if ((int)indexOptions >= (int)FieldIndexOptions.DocsAndFreqsAndPositionsAndOffsets)
            _hasOffsets = true;

        if (_hasPositions)
        {
            if (_hasOffsets && (startOffset != 0 || endOffset != 0))
            {
                _hasOffsets = true;
                Add(docId, position);
                StoreOffset(docId, _freqs[_count - 1] - 1, startOffset, endOffset);
                return;
            }
            Add(docId, position);
        }
        else
        {
            // DocsAndFreqs: track freq but not positions.
            AddDocAndFreq(docId);
        }
    }

    /// <summary>
    /// Adds a posting with a payload and the field's index options.
    /// Payloads are only stored when positions are enabled.
    /// </summary>
    public void AddWithPayload(int docId, int position, byte[]? payload, FieldIndexOptions indexOptions,
        int startOffset = 0, int endOffset = 0)
    {
        if ((int)indexOptions <= (int)FieldIndexOptions.DocsOnly)
        {
            AddDocOnly(docId);
            return;
        }

        _hasFreqs = true;
        if ((int)indexOptions >= (int)FieldIndexOptions.DocsAndFreqsAndPositions)
        {
            _hasPositions = true;
            if ((int)indexOptions >= (int)FieldIndexOptions.DocsAndFreqsAndPositionsAndOffsets)
                _hasOffsets = true;

            if (_hasOffsets && (startOffset != 0 || endOffset != 0))
            {
                _hasOffsets = true;
                AddWithPayload(docId, position, payload, startOffset, endOffset);
                return;
            }
            AddWithPayload(docId, position, payload);
        }
        else
        {
            // DocsAndFreqs with payload: store freq, discard payload (no positions to attach to).
            AddDocAndFreq(docId);
        }
    }

    /// <summary>
    /// Adds a doc ID + term frequency without position data.
    /// Used when <see cref="FieldIndexOptions.DocsAndFreqs"/> is configured.
    /// </summary>
    private void AddDocAndFreq(int docId)
    {
        if (IsLastDoc(docId))
        {
            _freqs[_count - 1]++;
            return;
        }
        if (_count == _docIdsLen) Grow();

        int delta = _count == 0 ? docId : docId - _lastAbsoluteDocId;
        _docIds[_count] = delta;
        _freqs[_count] = 1;
        _posStarts[_count] = NoPositionSentinel;
        _posByteLens[_count] = 0;
        _lastAbsoluteDocId = docId;
        _count++;
        InvalidateAbsoluteCache();
    }

    public void Add(int docId, int position)
    {
        _hasFreqs = true;
        _hasPositions = true;
        if (IsLastDoc(docId))
        {
            AppendDeltaToLastPosting(position - GetFirstPosition(_count - 1));
            _freqs[_count - 1]++;
            return;
        }
        ReadOnlySpan<int> single = stackalloc int[1] { position };
        AddNewPosting(docId, single);
    }

    public void Add(int docId, int position, int startOffset, int endOffset)
    {
        _hasOffsets = true;
        Add(docId, position);
        StoreOffset(docId, _freqs[_count - 1] - 1, startOffset, endOffset);
    }

    public void AddPositions(int docId, ReadOnlySpan<int> positions)
    {
        if (positions.IsEmpty) return;
        _hasFreqs = true;
        _hasPositions = true;
        if (IsLastDoc(docId))
        {
            int first = GetFirstPosition(_count - 1);
            Span<int> deltas = stackalloc int[positions.Length];
            for (int p = 0; p < positions.Length; p++)
                deltas[p] = positions[p] - first;
            AppendDeltasToLastPosting(deltas);
            _freqs[_count - 1] += positions.Length;
            return;
        }
        AddNewPosting(docId, positions);
    }

    public void AddDocOnly(int docId)
    {
        if (IsLastDoc(docId)) return;
        if (_count == _docIdsLen) Grow();

        int delta = _count == 0 ? docId : docId - _lastAbsoluteDocId;
        _docIds[_count] = delta;
        _freqs[_count] = 0;
        _posStarts[_count] = NoPositionSentinel;
        _posByteLens[_count] = 0;
        _lastAbsoluteDocId = docId;
        _count++;
        InvalidateAbsoluteCache();
    }

    public void AddPositionsWithPayloads(int docId, ReadOnlySpan<int> positions, byte[]?[] payloads)
    {
        if (positions.Length == 0) return;
        _hasFreqs = true;
        _hasPositions = true;
        EnsurePayloads();

        if (IsLastDoc(docId))
        {
            int idx = _count - 1;
            int first = GetFirstPosition(idx);
            Span<int> deltas = stackalloc int[positions.Length];
            for (int p = 0; p < positions.Length; p++)
                deltas[p] = positions[p] - first;
            AppendDeltasToLastPosting(deltas);
            _freqs[idx] += positions.Length;

            int freq = _freqs[idx];
            if (freq > _payloads![idx]!.Length) Array.Resize(ref _payloads[idx], freq);
            for (int p = 0; p < positions.Length; p++)
                _payloads[idx]![freq - positions.Length + p] = payloads[p];
        }
        else
        {
            AddNewPosting(docId, positions);
            int newIdx = _count - 1;
            _payloads![newIdx] = new byte[]?[positions.Length];
            Array.Copy(payloads, _payloads[newIdx], positions.Length);
        }
    }

    public void AddWithPayload(int docId, int position, byte[]? payload)
    {
        _hasFreqs = true;
        _hasPositions = true;
        EnsurePayloads();

        if (IsLastDoc(docId))
        {
            int idx = _count - 1;
            AppendDeltaToLastPosting(position - GetFirstPosition(idx));
            _freqs[idx]++;

            int freq = _freqs[idx];
            if (freq > _payloads![idx]!.Length) Array.Resize(ref _payloads[idx], freq);
            _payloads[idx]![freq - 1] = payload;
            return;
        }

        ReadOnlySpan<int> single = stackalloc int[1] { position };
        AddNewPosting(docId, single);
        int newIdx = _count - 1;
        _payloads![newIdx] = new byte[]?[1];
        _payloads[newIdx]![0] = payload;
    }

    public void AddWithPayload(int docId, int position, byte[]? payload, int startOffset, int endOffset)
    {
        _hasOffsets = true;
        AddWithPayload(docId, position, payload);
        StoreOffset(docId, -1, startOffset, endOffset);
    }

    private void EnsurePayloads()
    {
        if (_payloads is not null) return;
        _payloads = new byte[]?[_docIdsLen][];
        for (int i = 0; i < _count; i++)
        {
            int f = _freqs[i] > 0 ? _freqs[i] : 0;
            _payloads[i] = new byte[]?[f];
        }
    }

    private void EnsureOffsets()
    {
        if (_startOffsets is not null) return;
        _startOffsets = new int[_docIdsLen][];
        _endOffsets = new int[_docIdsLen][];
        for (int i = 0; i < _count; i++)
        {
            int f = _freqs[i] > 0 ? _freqs[i] : 0;
            _startOffsets[i] = new int[f];
            _endOffsets[i] = new int[f];
        }
    }

    private void StoreOffset(int docId, int positionIndex, int startOffset, int endOffset)
    {
        EnsureOffsets();
        if (IsLastDoc(docId))
        {
            int idx = _count - 1;
            int freq = _freqs[idx];
            int posIdx = positionIndex >= 0 ? positionIndex : freq - 1;
            _startOffsets![idx] ??= new int[freq];
            _endOffsets![idx] ??= new int[freq];
            if (freq > _startOffsets[idx]!.Length) Array.Resize(ref _startOffsets[idx], freq);
            if (freq > _endOffsets[idx]!.Length) Array.Resize(ref _endOffsets[idx], freq);
            _startOffsets[idx]![posIdx] = startOffset;
            _endOffsets[idx]![posIdx] = endOffset;
        }
        else
        {
            int idx = _count - 1;
            _startOffsets![idx] = new int[1] { startOffset };
            _endOffsets![idx] = new int[1] { endOffset };
        }
    }

    public int GetFreq(int index) => _freqs[index];

    public ReadOnlySpan<int> GetPositions(int index)
    {
        int start = _posStarts[index];
        if (start == NoPositionSentinel) return ReadOnlySpan<int>.Empty;
        int freq = _freqs[index];
        if (freq == 0) return ReadOnlySpan<int>.Empty;
        var src = _posBuf.AsSpan(start, _posByteLens[index]);
        var result = new int[freq];
        int pos = 0;

        pos += ReadVarInt(src, out int firstPos);
        result[0] = firstPos;

        for (int i = 1; i < freq; i++)
        {
            pos += ReadVarInt(src.Slice(pos), out int delta);
            result[i] = firstPos + delta;
        }
        return result;
    }

    public byte[]? GetPayload(int docIndex, int positionIndex)
    {
        if (_payloads == null || (uint)docIndex >= (uint)_count || _payloads[docIndex] == null)
            return null;
        var docPayloads = _payloads[docIndex];
        if ((uint)positionIndex >= (uint)docPayloads.Length)
            throw new ArgumentOutOfRangeException(nameof(positionIndex),
                $"Position index {positionIndex} is out of range for doc entry with {docPayloads.Length} positions.");
        return docPayloads[positionIndex];
    }

    public (int[]? Starts, int[]? Ends) GetOffsets(int index)
    {
        if (_startOffsets == null || (uint)index >= (uint)_count)
            return (null, null);
        return (_startOffsets[index], _endOffsets![index]);
    }

    public bool HasPayloads => _payloads != null;
    public bool HasOffsets => _hasOffsets;
    public bool HasFreqs => _hasFreqs;
    public bool HasPositions => _hasPositions;

    public void RemapDocIds(int[] inversePerm)
    {
        if (_count == 0) return;

        // Expand to absolute, remap, sort, re-delta
        if (!_absoluteCacheValid || _absoluteCache is null)
            ExpandToAbsolute();

        var entries = IntPool.Rent(_count);
        var origIdxs = IntPool.Rent(_count);
        for (int i = 0; i < _count; i++)
        {
            entries[i] = inversePerm[_absoluteCache![i]];
            origIdxs[i] = i;
        }
        Array.Sort(entries, origIdxs, 0, _count);

        var newFreqs = IntPool.Rent(_docIdsLen);
        var newPosStarts = IntPool.Rent(_docIdsLen);
        var newPosByteLens = IntPool.Rent(_docIdsLen);
        byte[]?[][]? newPayloads = _payloads is not null ? new byte[]?[_docIdsLen][] : null;
        int[][]? newStartOffsets = _startOffsets is not null ? new int[_docIdsLen][] : null;
        int[][]? newEndOffsets = _endOffsets is not null ? new int[_docIdsLen][] : null;
        var newPosBuf = BytePool.Rent(_posBufLen);
        int newPosBufUsed = 0;

        int prevAbs = 0;
        for (int i = 0; i < _count; i++)
        {
            int orig = origIdxs[i];
            int newAbs = entries[i];
            _docIds[i] = i == 0 ? newAbs : newAbs - prevAbs;
            prevAbs = newAbs;
            newFreqs[i] = _freqs[orig];

            int posStart = _posStarts[orig];
            int byteLen = _posByteLens[orig];

            if (posStart == NoPositionSentinel || _freqs[orig] == 0)
            {
                newPosStarts[i] = NoPositionSentinel;
                newPosByteLens[i] = 0;
            }
            else
            {
                newPosStarts[i] = newPosBufUsed;
                newPosByteLens[i] = byteLen;
                Array.Copy(_posBuf, posStart, newPosBuf, newPosBufUsed, byteLen);
                newPosBufUsed += byteLen;
            }
            if (newPayloads is not null)
                newPayloads[i] = _payloads![orig];
            if (newStartOffsets is not null)
                newStartOffsets[i] = _startOffsets![orig];
            if (newEndOffsets is not null)
                newEndOffsets[i] = _endOffsets![orig];
        }

        _lastAbsoluteDocId = prevAbs;
        InvalidateAbsoluteCache();

        IntPool.Return(entries);
        IntPool.Return(origIdxs);
        IntPool.Return(_freqs);
        IntPool.Return(_posStarts);
        IntPool.Return(_posByteLens);
        BytePool.Return(_posBuf);

        _freqs = newFreqs;
        _posStarts = newPosStarts;
        _posByteLens = newPosByteLens;
        _posBuf = newPosBuf;
        _posBufUsed = newPosBufUsed;
        _posBufLen = _posBuf.Length;
        _payloads = newPayloads;
        _startOffsets = newStartOffsets;
        _endOffsets = newEndOffsets;
        _cachedEstimatedBytes = RecomputeEstimatedBytes();

        // Invalidate absolute cache and re-expand with new remapped order
        _absoluteCacheValid = false;
    }

    public void ReturnBuffers()
    {
        if (_docIds.Length > 0) IntPool.Return(_docIds, clearArray: false);
        if (_freqs.Length > 0) IntPool.Return(_freqs, clearArray: false);
        if (_posStarts.Length > 0) IntPool.Return(_posStarts, clearArray: false);
        if (_posByteLens.Length > 0) IntPool.Return(_posByteLens, clearArray: false);
        if (_posBuf.Length > 0) BytePool.Return(_posBuf, clearArray: false);
        if (_absoluteCache is not null) IntPool.Return(_absoluteCache, clearArray: false);
        _docIds = []; _freqs = []; _posStarts = []; _posByteLens = [];
        _posBuf = []; _payloads = null; _absoluteCache = null;
        _startOffsets = null; _endOffsets = null;
        _count = 0; _docIdsLen = 0; _posBufLen = 0; _posBufUsed = 0;
        _lastAbsoluteDocId = -1;
        _hasFreqs = false; _hasPositions = false; _hasOffsets = false;
        _absoluteCacheValid = false;
        _cachedEstimatedBytes = 64;
    }

    private void Grow()
    {
        int newLen = _docIdsLen * 2;
        GrowIntArray(ref _docIds, _docIdsLen, newLen);
        GrowIntArray(ref _freqs, _docIdsLen, newLen);
        GrowIntArray(ref _posStarts, _docIdsLen, newLen);
        GrowIntArray(ref _posByteLens, _docIdsLen, newLen);
        if (_startOffsets != null) Array.Resize(ref _startOffsets, newLen);
        if (_endOffsets != null) Array.Resize(ref _endOffsets, newLen);
        if (_payloads != null) Array.Resize(ref _payloads, newLen);
        _docIdsLen = newLen;
        InvalidateAbsoluteCache();
        _cachedEstimatedBytes = RecomputeEstimatedBytes();
    }

    private static void GrowIntArray(ref int[] arr, int usedLength, int newMinLength)
    {
        var newArr = IntPool.Rent(newMinLength);
        if (usedLength > 0) Array.Copy(arr, newArr, usedLength);
        IntPool.Return(arr, clearArray: false);
        arr = newArr;
    }
}

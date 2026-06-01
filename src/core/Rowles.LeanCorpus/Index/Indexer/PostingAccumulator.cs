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

    private int[] _docIds;
    private int[] _freqs;
    private int[] _posStarts;      // byte offset into _posBuf per posting
    private int[] _posByteLens;    // byte length of encoded region per posting
    private byte[] _posBuf;
    private int _posBufUsed;       // bytes used in _posBuf
    private int _posBufLen;        // _posBuf array length
    private byte[]?[][]? _payloads;
    private int _count;
    private int _docIdsLen;        // logical length (may be < rented array length)
    private long _cachedEstimatedBytes;
    private bool _hasFreqs;
    private bool _hasPositions;

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
        _cachedEstimatedBytes = RecomputeEstimatedBytes();
    }

    public int Count => _count;

    public long EstimatedBytes => _cachedEstimatedBytes;

    private long RecomputeEstimatedBytes()
    {
        const long ObjectOverhead = 64;
        long bufferBytes = (long)(_docIds.Length + _freqs.Length + _posStarts.Length +
            _posByteLens.Length) * sizeof(int)
            + _posBuf.Length;
        return ObjectOverhead + bufferBytes;
    }

    /// <summary>Decodes the first absolute position from the posting's encoded bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetFirstPosition(int index)
    {
        int start = _posStarts[index];
        if (start == NoPositionSentinel) return 0;
        ReadVarInt(_posBuf.AsSpan(start), out int first);
        return first;
    }

    // ─────── VarInt helpers ───────

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
    /// Avoids the decode→re-encode cycle of <see cref="AddPositions"/> for merge paths.
    /// </summary>
    internal void AddEncodedPositions(int docId, int firstPosition, ReadOnlySpan<byte> deltaBytes, int freq)
    {
        _hasFreqs = true;
        _hasPositions = true;

        int firstBytes = VarIntEncodedSize(firstPosition);
        int totalBytes = firstBytes + deltaBytes.Length;

        if (_count > 0 && _docIds[_count - 1] == docId)
        {
            // Append deltas to last posting (merge case: same doc, additional positions)
            AppendDeltasToLastPostingRaw(deltaBytes, firstPosition);
            _freqs[_count - 1] += freq;
            return;
        }

        if (_count == _docIdsLen) Grow();
        EnsurePosBufCapacity(_posBufUsed + totalBytes);

        _docIds[_count] = docId;
        _freqs[_count] = freq;
        _posStarts[_count] = _posBufUsed;

        _posBufUsed += WriteVarInt(_posBuf.AsSpan(_posBufUsed), firstPosition);
        deltaBytes.CopyTo(_posBuf.AsSpan(_posBufUsed));
        _posBufUsed += deltaBytes.Length;
        _posByteLens[_count] = totalBytes;
        _count++;
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
            // Fast path: fits in-place at the end of the posting's region (common when contiguous)
            WriteVarInt(_posBuf.AsSpan(_posStarts[idx] + byteLen), delta);
            _posByteLens[idx] = byteLen + encodedSize;
            if (_posStarts[idx] + byteLen == _posBufUsed)
                _posBufUsed += encodedSize;
        }
        else
        {
            // Relocate to end of _posBuf
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

        // Always relocate — bulk append is the merge path, not hot
        EnsurePosBufCapacity(_posBufUsed + byteLen + extraBytes);
        Array.Copy(_posBuf, _posStarts[idx], _posBuf, _posBufUsed, byteLen);
        _posStarts[idx] = _posBufUsed;
        _posBufUsed += byteLen;
        for (int p = 0; p < deltas.Length; p++)
            _posBufUsed += WriteVarInt(_posBuf.AsSpan(_posBufUsed), deltas[p]);
        _posByteLens[idx] = byteLen + extraBytes;
    }

    private void AddNewPosting(int docId, ReadOnlySpan<int> positions)
    {
        if (_count == _docIdsLen) Grow();

        int firstPos = positions[0];
        int totalBytes = VarIntEncodedSize(firstPos);
        for (int p = 1; p < positions.Length; p++)
            totalBytes += VarIntEncodedSize(positions[p] - firstPos);

        EnsurePosBufCapacity(_posBufUsed + totalBytes);

        _docIds[_count] = docId;
        _freqs[_count] = positions.Length;
        _posStarts[_count] = _posBufUsed;

        _posBufUsed += WriteVarInt(_posBuf.AsSpan(_posBufUsed), firstPos);
        for (int p = 1; p < positions.Length; p++)
            _posBufUsed += WriteVarInt(_posBuf.AsSpan(_posBufUsed), positions[p] - firstPos);

        _posByteLens[_count] = totalBytes;
        _count++;
    }

    // ─────── Public API ───────

    public void Add(int docId, int position)
    {
        _hasFreqs = true;
        _hasPositions = true;
        if (_count > 0 && _docIds[_count - 1] == docId)
        {
            AppendDeltaToLastPosting(position - GetFirstPosition(_count - 1));
            _freqs[_count - 1]++;
            return;
        }
        ReadOnlySpan<int> single = stackalloc int[1] { position };
        AddNewPosting(docId, single);
    }


    public void AddPositions(int docId, ReadOnlySpan<int> positions)
    {
        if (positions.IsEmpty) return;
        _hasFreqs = true;
        _hasPositions = true;
        if (_count > 0 && _docIds[_count - 1] == docId)
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
        if (_count > 0 && _docIds[_count - 1] == docId) return;
        if (_count == _docIdsLen) Grow();
        _docIds[_count] = docId;
        _freqs[_count] = 0;
        _posStarts[_count] = NoPositionSentinel;
        _posByteLens[_count] = 0;
        _count++;
    }

    public void AddPositionsWithPayloads(int docId, ReadOnlySpan<int> positions, byte[]?[] payloads)
    {
        if (positions.Length == 0) return;
        _hasFreqs = true;
        _hasPositions = true;
        EnsurePayloads();

        if (_count > 0 && _docIds[_count - 1] == docId)
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

        if (_count > 0 && _docIds[_count - 1] == docId)
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

    public ReadOnlySpan<int> DocIds => _docIds.AsSpan(0, _count);
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

        // First position: absolute VarInt
        pos += ReadVarInt(src, out int firstPos);
        result[0] = firstPos;

        // Subsequent: VarInt deltas from first
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

    public bool HasPayloads => _payloads != null;
    public bool HasFreqs => _hasFreqs;
    public bool HasPositions => _hasPositions;

    public void RemapDocIds(int[] inversePerm)
    {
        if (_count == 0) return;

        var entries = IntPool.Rent(_count);
        var origIdxs = IntPool.Rent(_count);
        for (int i = 0; i < _count; i++)
        {
            entries[i] = inversePerm[_docIds[i]];
            origIdxs[i] = i;
        }
        Array.Sort(entries, origIdxs, 0, _count);

        var newFreqs = IntPool.Rent(_docIdsLen);
        var newPosStarts = IntPool.Rent(_docIdsLen);
        var newPosByteLens = IntPool.Rent(_docIdsLen);
        byte[]?[][]? newPayloads = _payloads is not null ? new byte[]?[_docIdsLen][] : null;

        var newPosBuf = BytePool.Rent(_posBufLen);
        int newPosBufUsed = 0;

        for (int i = 0; i < _count; i++)
        {
            int orig = origIdxs[i];
            _docIds[i] = entries[i];
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
        }

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
        _cachedEstimatedBytes = RecomputeEstimatedBytes();
    }

    public void ReturnBuffers()
    {
        if (_docIds.Length > 0) IntPool.Return(_docIds, clearArray: false);
        if (_freqs.Length > 0) IntPool.Return(_freqs, clearArray: false);
        if (_posStarts.Length > 0) IntPool.Return(_posStarts, clearArray: false);
        if (_posByteLens.Length > 0) IntPool.Return(_posByteLens, clearArray: false);
        if (_posBuf.Length > 0) BytePool.Return(_posBuf, clearArray: false);
        _docIds = []; _freqs = []; _posStarts = []; _posByteLens = [];
        _posBuf = []; _payloads = null;
        _count = 0; _docIdsLen = 0; _posBufLen = 0; _posBufUsed = 0;
        _hasFreqs = false; _hasPositions = false;
        _cachedEstimatedBytes = 64;
    }

    private void Grow()
    {
        int newLen = _docIdsLen * 2;
        GrowIntArray(ref _docIds, _docIdsLen, newLen);
        GrowIntArray(ref _freqs, _docIdsLen, newLen);
        GrowIntArray(ref _posStarts, _docIdsLen, newLen);
        GrowIntArray(ref _posByteLens, _docIdsLen, newLen);
        if (_payloads != null) Array.Resize(ref _payloads, newLen);
        _docIdsLen = newLen;
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

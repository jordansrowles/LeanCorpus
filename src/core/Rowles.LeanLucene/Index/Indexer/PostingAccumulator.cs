using System.Buffers;

namespace Rowles.LeanLucene.Index.Indexer;

/// <summary>
/// Accumulates doc IDs, term frequencies, and positions for a single qualified term
/// during indexing. Uses ArrayPool-backed buffers to avoid GC pressure from repeated
/// resize-copy-abandon cycles. Call <see cref="ReturnBuffers"/> after flush to return
/// rented arrays.
/// </summary>
internal sealed class PostingAccumulator
{
    private static readonly ArrayPool<int> Pool = ArrayPool<int>.Shared;

    private int[] _docIds;
    private int[] _freqs;
    private int[] _posStarts;
    private int[] _posLengths;
    private int[] _posBuf;
    private int _posBufUsed;
    private byte[]?[][]? _payloads;
    private int _count;
    private int _docIdsLen; // logical length (may be < rented array length)
    private int _posBufLen;
    private long _cachedEstimatedBytes;

    private const int NoPositionSentinel = -1;

    public PostingAccumulator()
    {
        _docIds = Pool.Rent(4);
        _freqs = Pool.Rent(4);
        _posStarts = Pool.Rent(4);
        _posLengths = Pool.Rent(4);
        _posBuf = Pool.Rent(8);
        _docIdsLen = 4;
        _posBufLen = 8;
        _posBufUsed = 0;
        _count = 0;
        _cachedEstimatedBytes = RecomputeEstimatedBytes();
    }

    public int Count => _count;

    /// <summary>
    /// Estimated heap bytes consumed by this accumulator's pooled buffers,
    /// plus a fixed 64-byte overhead for the object itself.
    /// Cached and updated only when buffers grow, so reads are O(1).
    /// </summary>
    public long EstimatedBytes => _cachedEstimatedBytes;

    private long RecomputeEstimatedBytes()
    {
        const long ObjectOverhead = 64;
        long bufferBytes = (long)(_docIds.Length + _freqs.Length + _posStarts.Length + _posLengths.Length + _posBuf.Length) * sizeof(int);
        return ObjectOverhead + bufferBytes;
    }

    public void Add(int docId, int position)
    {
        if (_count > 0 && _docIds[_count - 1] == docId)
        {
            _freqs[_count - 1]++;
            int freq = _freqs[_count - 1];
            int start = _posStarts[_count - 1];
            int allocated = _posLengths[_count - 1];
            if (freq > allocated)
            {
                int newAllocated = allocated * 2;
                EnsurePosBufCapacity(_posBufUsed + newAllocated);
                int newStart = _posBufUsed;
                Array.Copy(_posBuf, start, _posBuf, newStart, freq - 1);
                _posStarts[_count - 1] = newStart;
                _posLengths[_count - 1] = newAllocated;
                _posBuf[newStart + freq - 1] = position;
                _posBufUsed += newAllocated;
            }
            else
            {
                _posBuf[start + freq - 1] = position;
            }
            return;
        }

        if (_count == _docIdsLen)
            Grow();

        EnsurePosBufCapacity(_posBufUsed + 1);
        _docIds[_count] = docId;
        _freqs[_count] = 1;
        _posStarts[_count] = _posBufUsed;
        _posLengths[_count] = 1;
        _posBuf[_posBufUsed] = position;
        _posBufUsed += 1;
        _count++;
    }

    public void AddWithPayload(int docId, int position, byte[]? payload)
    {
        if (_payloads == null)
        {
            _payloads = new byte[]?[_docIdsLen][];
            for (int i = 0; i < _count; i++)
            {
                int freq = _freqs[i] > 0 ? _freqs[i] : 0;
                _payloads[i] = new byte[]?[freq];
            }
        }

        if (_count > 0 && _docIds[_count - 1] == docId)
        {
            _freqs[_count - 1]++;
            int freq = _freqs[_count - 1];
            int start = _posStarts[_count - 1];
            int allocated = _posLengths[_count - 1];
            if (freq > allocated)
            {
                int newAllocated = allocated * 2;
                EnsurePosBufCapacity(_posBufUsed + newAllocated);
                int newStart = _posBufUsed;
                Array.Copy(_posBuf, start, _posBuf, newStart, freq - 1);
                _posStarts[_count - 1] = newStart;
                _posLengths[_count - 1] = newAllocated;
                _posBuf[newStart + freq - 1] = position;
                _posBufUsed += newAllocated;
                Array.Resize(ref _payloads[_count - 1], newAllocated);
            }
            else
            {
                _posBuf[start + freq - 1] = position;
            }
            _payloads[_count - 1][freq - 1] = payload;
            return;
        }

        if (_count == _docIdsLen)
            Grow();

        EnsurePosBufCapacity(_posBufUsed + 1);
        _docIds[_count] = docId;
        _freqs[_count] = 1;
        _posStarts[_count] = _posBufUsed;
        _posLengths[_count] = 1;
        _posBuf[_posBufUsed] = position;
        _posBufUsed += 1;
        _payloads[_count] = new byte[]?[1];
        _payloads[_count][0] = payload;
        _count++;
    }

    public void AddDocOnly(int docId)
    {
        if (_count > 0 && _docIds[_count - 1] == docId)
            return;

        if (_count == _docIdsLen)
            Grow();

        _docIds[_count] = docId;
        _freqs[_count] = 0;
        _posStarts[_count] = NoPositionSentinel;
        _posLengths[_count] = 0;
        _count++;
    }

    public ReadOnlySpan<int> DocIds => _docIds.AsSpan(0, _count);

    public int GetFreq(int index) => _freqs[index];

    public ReadOnlySpan<int> GetPositions(int index)
    {
        int start = _posStarts[index];
        if (start == NoPositionSentinel) return ReadOnlySpan<int>.Empty;
        return _posBuf.AsSpan(start, _freqs[index]);
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

    public bool HasFreqs
    {
        get
        {
            for (int i = 0; i < _count; i++)
                if (_freqs[i] > 0) return true;
            return false;
        }
    }

    public bool HasPositions
    {
        get
        {
            for (int i = 0; i < _count; i++)
                if (_posStarts[i] != NoPositionSentinel && _freqs[i] > 0) return true;
            return false;
        }
    }

    /// <summary>
    /// Translates doc IDs using the inverse permutation and re-sorts entries so
    /// doc IDs remain in ascending order (required by the postings codec).
    /// </summary>
    public void RemapDocIds(int[] inversePerm)
    {
        if (_count == 0) return;

        // Build (newDocId, originalIndex) pairs, sort by newDocId
        var entries = Pool.Rent(_count);
        var origIdxs = Pool.Rent(_count);
        for (int i = 0; i < _count; i++)
        {
            entries[i] = inversePerm[_docIds[i]];
            origIdxs[i] = i;
        }
        Array.Sort(entries, origIdxs, 0, _count);

        // Rebuild parallel arrays in sorted order using temp buffers
        var newFreqs = Pool.Rent(_docIdsLen);
        var newPosStarts = Pool.Rent(_docIdsLen);
        var newPosLengths = Pool.Rent(_docIdsLen);
        byte[]?[][]? newPayloads = _payloads is not null ? new byte[]?[_docIdsLen][] : null;

        // Compact positions into a new flat buffer
        var newPosBuf = Pool.Rent(_posBufLen);
        int newPosBufUsed = 0;

        for (int i = 0; i < _count; i++)
        {
            int orig = origIdxs[i];
            _docIds[i] = entries[i];
            newFreqs[i] = _freqs[orig];
            int posStart = _posStarts[orig];
            int freq = _freqs[orig];
            if (posStart == NoPositionSentinel || freq == 0)
            {
                newPosStarts[i] = NoPositionSentinel;
                newPosLengths[i] = 0;
            }
            else
            {
                newPosStarts[i] = newPosBufUsed;
                newPosLengths[i] = freq;
                Array.Copy(_posBuf, posStart, newPosBuf, newPosBufUsed, freq);
                newPosBufUsed += freq;
            }
            if (newPayloads is not null)
                newPayloads[i] = _payloads![orig];
        }

        Pool.Return(entries);
        Pool.Return(origIdxs);
        Pool.Return(_freqs);
        Pool.Return(_posStarts);
        Pool.Return(_posLengths);
        Pool.Return(_posBuf);

        _freqs = newFreqs;
        _posStarts = newPosStarts;
        _posLengths = newPosLengths;
        _posBuf = newPosBuf;
        _posBufUsed = newPosBufUsed;
        _posBufLen = _posBuf.Length;
        _payloads = newPayloads;
        _cachedEstimatedBytes = RecomputeEstimatedBytes();
    }

    /// <summary>Returns all pooled arrays. Call once after flush; do not use the accumulator afterwards.</summary>
    public void ReturnBuffers()
    {
        if (_docIds.Length > 0) Pool.Return(_docIds, clearArray: false);
        if (_freqs.Length > 0) Pool.Return(_freqs, clearArray: false);
        if (_posStarts.Length > 0) Pool.Return(_posStarts, clearArray: false);
        if (_posLengths.Length > 0) Pool.Return(_posLengths, clearArray: false);
        if (_posBuf.Length > 0) Pool.Return(_posBuf, clearArray: false);
        _docIds = [];
        _freqs = [];
        _posStarts = [];
        _posLengths = [];
        _posBuf = [];
        _payloads = null;
        _count = 0;
        _docIdsLen = 0;
        _posBufLen = 0;
        _posBufUsed = 0;
        _cachedEstimatedBytes = 64; // object overhead only
    }

    private void Grow()
    {
        int newLen = _docIdsLen * 2;
        GrowArray(ref _docIds, _docIdsLen, newLen);
        GrowArray(ref _freqs, _docIdsLen, newLen);
        GrowArray(ref _posStarts, _docIdsLen, newLen);
        GrowArray(ref _posLengths, _docIdsLen, newLen);
        if (_payloads != null)
            Array.Resize(ref _payloads, newLen);
        _docIdsLen = newLen;
        _cachedEstimatedBytes = RecomputeEstimatedBytes();
    }

    private void EnsurePosBufCapacity(int required)
    {
        if (required <= _posBufLen) return;
        int newLen = Math.Max(_posBufLen * 2, required);
        GrowArray(ref _posBuf, _posBufUsed, newLen);
        _posBufLen = newLen;
        _cachedEstimatedBytes = RecomputeEstimatedBytes();
    }

    private static void GrowArray(ref int[] arr, int usedLength, int newMinLength)
    {
        var newArr = Pool.Rent(newMinLength);
        if (usedLength > 0)
            Array.Copy(arr, newArr, usedLength);
        Pool.Return(arr, clearArray: false);
        arr = newArr;
    }
}

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Codecs.Postings;

/// <summary>
/// Block-at-a-time postings iterator (v3 format). Reads packed blocks of 128 doc IDs
/// written by <see cref="BlockPostingsWriter"/>. Only the current block is decoded,
/// keeping memory at a constant ~1 KB (2 × 128 ints) regardless of postings list length.
/// </summary>
public struct BlockPostingsEnum : IDisposable
{
    private const int BlockSize = PackedIntCodec.BlockSize;
    /// <summary>Sentinel returned by <see cref="DocId"/> once the iterator is exhausted.</summary>
    public const int NoMoreDocs = int.MaxValue;
    private const int MaxPreloadedSkipEntries = 4096;

    // Doc file
    private readonly IndexInput _docInput;
    private readonly long _docStartOffset;
    private readonly int _docFreq;

    // Skip data
    private readonly SkipEntry[] _skipEntries;
    private readonly int _skipCount;
    private readonly bool _hasPositions;

    // Current block state
    private readonly int[] _docIdBlock;
    private readonly int[] _freqBlock;
    private int _blockStart;        // index in overall posting list of first doc in current block
    private int _blockCount;        // number of docs in current block (128 or tail)
    private int _indexInBlock;      // position within current block
    private int _currentBlockIndex; // which block we decoded (-1 = none yet)
    private bool _exhausted;
    private long _nextBlockOffset; // absolute file offset of the next block to decode
    private long _cursorPosition;  // per-call read cursor; never shared with callers

    /// <summary>Current doc ID, or <see cref="NoMoreDocs"/> if exhausted.</summary>
    public int DocId => _exhausted ? NoMoreDocs :
        (_indexInBlock >= 0 && _indexInBlock < _blockCount ? _docIdBlock[_indexInBlock] : NoMoreDocs);

    /// <summary>Frequency of current doc (freq-1 stored, we add 1 back).</summary>
    public int Freq => _exhausted ? 0 :
        (_indexInBlock >= 0 && _indexInBlock < _blockCount ? _freqBlock[_indexInBlock] + 1 : 0);

    /// <summary>Total number of documents in this posting list.</summary>
    public int DocFreqCount => _docFreq;

    /// <summary>True once every block has been consumed and no further docs are available.</summary>
    public bool IsExhausted => _exhausted;

    /// <summary>Returns the skip entries for WAND scoring. Each entry has MaxFreqInBlock and MaxNormInBlock.</summary>
    internal ReadOnlySpan<SkipEntry> SkipEntries => _skipEntries.AsSpan(0, _skipCount);

    /// <summary>Returns the current block index (0-based). -1 if no block decoded yet.</summary>
    public int CurrentBlockIndex => _currentBlockIndex;

    /// <summary>Returns the current zero-based index in the overall postings list.</summary>
    public int CurrentGlobalIndex => _blockStart + _indexInBlock;

    /// <summary>
    /// Creates a BlockPostingsEnum positioned before the first document.
    /// Call <see cref="NextDoc"/> to advance to the first document.
    /// </summary>
    public static BlockPostingsEnum Create(IndexInput docInput, long docStartOffset,
        long skipOffset, int docFreq, bool hasPositions = false)
    {
        // Use a local cursor so we do not mutate the shared docInput._position.
        long cursor = skipOffset;
        int skipCount = docInput.ReadInt32(ref cursor);
        int loadCount = Math.Min(skipCount, MaxPreloadedSkipEntries);
        var skipEntries = ArrayPool<SkipEntry>.Shared.Rent(Math.Max(loadCount, 1));
        for (int i = 0; i < loadCount; i++)
        {
            skipEntries[i] = new SkipEntry
            {
                LastDocId = docInput.ReadInt32(ref cursor),
                DocByteOffset = docInput.ReadInt64(ref cursor),
                PosFileOffset = hasPositions ? docInput.ReadInt64(ref cursor) : 0,
                MaxFreqInBlock = (ushort)(docInput.ReadByte(ref cursor) | (docInput.ReadByte(ref cursor) << 8)),
                MaxNormInBlock = docInput.ReadByte(ref cursor)
            };
        }

        if (skipCount > loadCount)
        {
            int bytesPerEntry = hasPositions ? 23 : 15;
            cursor += (long)(skipCount - loadCount) * bytesPerEntry;
        }

        return new BlockPostingsEnum(docInput, docStartOffset, docFreq,
            skipEntries, loadCount, hasPositions);
    }

    private BlockPostingsEnum(IndexInput docInput, long docStartOffset, int docFreq,
        SkipEntry[] skipEntries, int skipCount, bool hasPositions)
    {
        _docInput = docInput;
        _docStartOffset = docStartOffset;
        _docFreq = docFreq;
        _skipEntries = skipEntries;
        _skipCount = skipCount;
        _hasPositions = hasPositions;

        _docIdBlock = ArrayPool<int>.Shared.Rent(BlockSize);
        _freqBlock = ArrayPool<int>.Shared.Rent(BlockSize);

        _blockStart = 0;
        _blockCount = 0;
        _indexInBlock = -1;
        _currentBlockIndex = -1;
        _exhausted = docFreq == 0;
        _nextBlockOffset = docStartOffset;
        _cursorPosition = docStartOffset;
    }

    /// <summary>
    /// Advances to the next document. Returns the doc ID, or <see cref="NoMoreDocs"/>.
    /// </summary>
    public int NextDoc()
    {
        if (_exhausted) return NoMoreDocs;

        _indexInBlock++;

        if (_indexInBlock >= _blockCount)
        {
            // Finished current block — decode next
            int consumed = _blockStart + _blockCount;
            if (consumed >= _docFreq)
            {
                _exhausted = true;
                return NoMoreDocs;
            }
            _blockStart = consumed;
            DecodeBlockAt(_blockStart);
            _indexInBlock = 0;
        }

        return DocId;
    }

    /// <summary>
    /// Advances to the first document with ID ≥ <paramref name="target"/>.
    /// Uses skip data for O(log N) seeking across blocks and branchless
    /// SIMD scanning within blocks.
    /// </summary>
    public int Advance(int target)
    {
        if (_exhausted) return NoMoreDocs;

        // If target is within the already-decoded current block, scan forward
        if (_blockCount > 0 && _indexInBlock < _blockCount &&
            _docIdBlock[_blockCount - 1] >= target)
        {
            _indexInBlock = BranchlessAdvanceInBlock(target, _indexInBlock, _blockCount);
            if (_indexInBlock < _blockCount)
                return DocId;
        }

        // Use skip data to find the right block
        int targetBlockIdx = FindSkipBlock(target);

        if (targetBlockIdx >= 0)
        {
            int blockStart = targetBlockIdx * BlockSize;
            if (blockStart != _blockStart || _blockCount == 0)
            {
                _cursorPosition = _docStartOffset + _skipEntries[targetBlockIdx].DocByteOffset;
                _blockStart = blockStart;
                _currentBlockIndex = targetBlockIdx;
                DecodeFullBlockAtCurrentPosition();
                _nextBlockOffset = _cursorPosition;
                _indexInBlock = 0;
            }

            // Branchless scan within block
            _indexInBlock = BranchlessAdvanceInBlock(target, _indexInBlock, _blockCount);
            if (_indexInBlock < _blockCount)
                return DocId;
        }

        // Fall back to linear NextDoc scan (for tail or if skip data didn't help)
        while (true)
        {
            int doc = NextDoc();
            if (doc >= target || doc == NoMoreDocs)
                return doc;
        }
    }

    /// <summary>
    /// Branchless advance within a decoded block: finds the first index where
    /// <c>_docIdBlock[index] &gt;= target</c> using SIMD or conditional-move patterns.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int BranchlessAdvanceInBlock(int target, int start, int count)
    {
#if NET11_0_OR_GREATER
        return BranchlessAdvanceSimd(target, start, count);
#else
        if (Vector128.IsHardwareAccelerated)
            return BranchlessAdvanceSimd(target, start, count);
        return BranchlessAdvanceScalar(target, start, count);
#endif
    }

    /// <summary>
    /// SIMD branchless advance: loads 4 doc IDs at a time, compares against target,
    /// uses ExtractMostSignificantBits + TrailingZeroCount to find first match.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int BranchlessAdvanceSimd(int target, int start, int count)
    {
        var targetVec = Vector128.Create(target);
        int i = start;

        // Process 4 elements at a time
        int simdLimit = count - 3;
        while (i < simdLimit)
        {
            var block = Vector128.Create(
                _docIdBlock[i], _docIdBlock[i + 1],
                _docIdBlock[i + 2], _docIdBlock[i + 3]);

            // Compare: each lane becomes -1 (0xFFFFFFFF) if block[lane] >= target
            // GreaterThanOrEqual is (block >= target), mask bits set where true
            var geq = Vector128.GreaterThanOrEqual(block, targetVec);
            uint bits = geq.ExtractMostSignificantBits();

            if (bits != 0)
                return i + System.Numerics.BitOperations.TrailingZeroCount(bits);

            i += 4;
        }

        // Scalar tail
        while (i < count && _docIdBlock[i] < target)
            i++;
        return i;
    }

    /// <summary>
    /// Scalar branchless advance using conditional-move-friendly pattern.
    /// The JIT emits cmov for <c>x = condition ? a : b</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int BranchlessAdvanceScalar(int target, int start, int count)
    {
        int i = start;
        while (i < count && _docIdBlock[i] < target)
            i++;
        return i;
    }

    /// <summary>Finds the first skip entry whose LastDocId ≥ target using binary search.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindSkipBlock(int target)
    {
        if (_skipCount == 0) return -1;

        int lo = 0, hi = _skipCount - 1;

        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (_skipEntries[mid].LastDocId < target)
                lo = mid + 1;
            else
                hi = mid - 1;
        }

        // lo is the first index where LastDocId >= target, or _skipCount if none
        return lo < _skipCount ? lo : -1;
    }

    private void DecodeBlockAt(int blockStart)
    {
        int blockIndex = blockStart / BlockSize;
        int remaining = _docFreq - blockStart;

        if (blockIndex < _skipCount)
            _cursorPosition = _docStartOffset + _skipEntries[blockIndex].DocByteOffset;
        else
            _cursorPosition = _nextBlockOffset;

        _currentBlockIndex = blockIndex;

        if (remaining >= BlockSize && blockIndex < _skipCount)
            DecodeFullBlockAtCurrentPosition();
        else
            DecodeTailAtCurrentPosition();

        _nextBlockOffset = _cursorPosition;
    }

    private void DecodeFullBlockAtCurrentPosition()
    {
        // On-disk format per block:
        // DocIDs: [numBits:1byte][packed data: numBits*16 bytes]
        // Freqs:  [numBits:1byte][packed data: numBits*16 bytes]

        // Decode doc IDs (delta-encoded)
        int docNumBits = _docInput.ReadByte(ref _cursorPosition);
        int docPackedBytes = docNumBits * 16;
        int prevDocId = _currentBlockIndex > 0
            ? _skipEntries[_currentBlockIndex - 1].LastDocId : 0;

        if (docNumBits == 0)
        {
            // All deltas are zero — fill with prevDocId (shouldn't happen in practice)
            Array.Fill(_docIdBlock, prevDocId, 0, BlockSize);
        }
        else
        {
            var docData = _docInput.ReadSpan(docPackedBytes, ref _cursorPosition);
            PackedIntCodec.UnpackDelta(docData, docNumBits, prevDocId, _docIdBlock);
        }

        // Decode frequencies (stored as freq-1, bit-packed with embedded numBits header)
        int freqNumBits = _docInput.ReadByte(ref _cursorPosition);
        if (freqNumBits == 0)
        {
            Array.Fill(_freqBlock, 0, 0, BlockSize); // all freq-1 = 0, i.e. freq = 1
        }
        else
        {
            int freqPackedBytes = freqNumBits * 16;
            var freqData = _docInput.ReadSpan(freqPackedBytes, ref _cursorPosition);
            PackedIntCodec.Unpack(freqData, freqNumBits, _freqBlock);
        }

        _blockCount = BlockSize;
    }

    private void DecodeTailAtCurrentPosition()
    {
        int tailCount = _docInput.ReadVarInt(ref _cursorPosition);
        int prevDocId = _currentBlockIndex > 0 && _currentBlockIndex <= _skipCount
            ? _skipEntries[_currentBlockIndex - 1].LastDocId : 0;

        for (int i = 0; i < tailCount; i++)
        {
            int delta = _docInput.ReadVarIntFast(ref _cursorPosition);
            prevDocId += delta;
            _docIdBlock[i] = prevDocId;
        }

        // Validate monotonically increasing doc IDs (catches overflow from corrupt data)
        if (tailCount > 0 && _docIdBlock[tailCount - 1] < 0)
            throw new InvalidDataException(
                "Postings data is corrupt: doc ID delta overflow in tail block.");

        for (int i = 0; i < tailCount; i++)
            _freqBlock[i] = _docInput.ReadVarIntFast(ref _cursorPosition);

        _blockCount = tailCount;
    }

    /// <summary>Returns all rented buffers back to <see cref="System.Buffers.ArrayPool{T}"/>.</summary>
    public void Dispose()
    {
        if (_docIdBlock is not null)
            ArrayPool<int>.Shared.Return(_docIdBlock);
        if (_freqBlock is not null)
            ArrayPool<int>.Shared.Return(_freqBlock);
        if (_skipEntries is not null)
            ArrayPool<SkipEntry>.Shared.Return(_skipEntries);
    }
}

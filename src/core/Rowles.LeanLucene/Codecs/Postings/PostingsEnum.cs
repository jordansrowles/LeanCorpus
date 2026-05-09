using System.Buffers;
using System.Runtime.CompilerServices;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Codecs.Postings;

/// <summary>
/// Forward-only cursor over a postings list. Decodes doc IDs and frequencies
/// once into ArrayPool-rented buffers, then yields (DocId, Freq) pairs via MoveNext().
/// Optionally decodes positions when created via <see cref="CreateWithPositions"/>.
/// 
/// <para><b>Lifetime contract:</b> When using the lazy position path, this struct holds a raw
/// <c>byte*</c> pointer into a memory-mapped <see cref="IndexInput"/>. The source input
/// (<see cref="_sourceInput"/>) must remain open and un-disposed for the entire lifetime
/// of this PostingsEnum. Callers must not dispose the IndexInput while any PostingsEnum
/// referencing it is still alive.</para>
/// </summary>
public unsafe struct PostingsEnum : IDisposable
{
    private int[]? _docIds;
    private int[]? _freqs;
    private readonly int _count;
    private int _index;
    private bool _disposed;
    private int[]? _positionData;
    private int[]? _positionStarts;

    // Lazy block-at-a-time mode (v3): delegates to BlockPostingsEnum
    // instead of pre-decoding all doc IDs/frequencies into ArrayPool arrays.
    private BlockPostingsEnum _blockEnum;
    private bool _lazyMode;

    // Lazy position decoding: store per-doc byte offsets and base pointer
    private long[]? _positionByteOffsets;
    private int[]? _positionCounts;
    private byte* _posBasePtr;
    private int[]? _lazyPosBuffer;
    private int _lastDecodedPosIndex; // cache: skip re-decode if same doc
    private int _lastDecodedPosCount; // number of valid positions in _lazyPosBuffer
    // Prevents the IndexInput from being disposed/GC'd while this PostingsEnum holds a raw pointer
    private IndexInput? _sourceInput;

    // v2 payload support
    private bool _hasPayloads;
    private long[]? _payloadByteOffsets;
    private int[]? _payloadLengths;

    // Thread-static recycled buffer pools for position metadata arrays.
    // A 4-slot pool allows reuse when phrase queries create multiple PostingsEnums
    // on the same thread before any are disposed (typical 3-word phrase = 3 PEs).
    private const int PoolCapacity = 4;
    [ThreadStatic] private static long[]?[]? t_posOffsetsPool;
    [ThreadStatic] private static int[]?[]? t_posCountsPool;
    [ThreadStatic] private static int[]?[]? t_lazyPosPool;

    // Do not recycle arrays larger than 1M entries (8 MB for long[], 4 MB for int[]).
    private const int MaxRecycledLength = 1_048_576;
    private const int MaxPositionPreloadDocs = 65_536;

    private static long[] RentPosOffsets(int minLength)
    {
        var pool = t_posOffsetsPool;
        if (pool is not null)
        {
            for (int i = 0; i < PoolCapacity; i++)
            {
                var buf = pool[i];
                if (buf is not null && buf.Length >= minLength)
                {
                    pool[i] = null;
                    return buf;
                }
            }
        }
        return ArrayPool<long>.Shared.Rent(minLength);
    }

    private static int[] RentPosCounts(int minLength)
    {
        var pool = t_posCountsPool;
        if (pool is not null)
        {
            for (int i = 0; i < PoolCapacity; i++)
            {
                var buf = pool[i];
                if (buf is not null && buf.Length >= minLength)
                {
                    pool[i] = null;
                    return buf;
                }
            }
        }
        return ArrayPool<int>.Shared.Rent(minLength);
    }

    private static int[] RentLazyPosBuffer(int minLength)
    {
        var pool = t_lazyPosPool;
        if (pool is not null)
        {
            for (int i = 0; i < PoolCapacity; i++)
            {
                var buf = pool[i];
                if (buf is not null && buf.Length >= minLength)
                {
                    pool[i] = null;
                    return buf;
                }
            }
        }
        return ArrayPool<int>.Shared.Rent(minLength);
    }

    private static void ReturnPosOffsets(long[] buffer)
    {
        if (buffer.Length > MaxRecycledLength)
        {
            ArrayPool<long>.Shared.Return(buffer);
            return;
        }
        var pool = t_posOffsetsPool ??= new long[PoolCapacity][];
        for (int i = 0; i < PoolCapacity; i++)
        {
            if (pool[i] is null) { pool[i] = buffer; return; }
        }
        // Pool full: evict smallest, keep the larger buffer for future reuse
        int smallest = 0;
        for (int i = 1; i < PoolCapacity; i++)
            if (pool[i]!.Length < pool[smallest]!.Length) smallest = i;
        ArrayPool<long>.Shared.Return(pool[smallest]!);
        pool[smallest] = buffer;
    }

    private static void ReturnPosCounts(int[] buffer)
    {
        if (buffer.Length > MaxRecycledLength)
        {
            ArrayPool<int>.Shared.Return(buffer);
            return;
        }
        var pool = t_posCountsPool ??= new int[PoolCapacity][];
        for (int i = 0; i < PoolCapacity; i++)
        {
            if (pool[i] is null) { pool[i] = buffer; return; }
        }
        int smallest = 0;
        for (int i = 1; i < PoolCapacity; i++)
            if (pool[i]!.Length < pool[smallest]!.Length) smallest = i;
        ArrayPool<int>.Shared.Return(pool[smallest]!);
        pool[smallest] = buffer;
    }

    private static void ReturnLazyPosBuffer(int[] buffer)
    {
        if (buffer.Length > MaxRecycledLength)
        {
            ArrayPool<int>.Shared.Return(buffer);
            return;
        }
        var pool = t_lazyPosPool ??= new int[PoolCapacity][];
        for (int i = 0; i < PoolCapacity; i++)
        {
            if (pool[i] is null) { pool[i] = buffer; return; }
        }
        int smallest = 0;
        for (int i = 1; i < PoolCapacity; i++)
            if (pool[i]!.Length < pool[smallest]!.Length) smallest = i;
        ArrayPool<int>.Shared.Return(pool[smallest]!);
        pool[smallest] = buffer;
    }

    /// <summary>Gets the total number of documents in this postings list.</summary>
    public int DocFreq => _lazyMode ? _blockEnum.DocFreqCount : _count;

    /// <summary>Gets the current document ID, or -1 if the cursor has not been advanced or is exhausted.</summary>
    public int DocId => _lazyMode ? (_blockEnum.IsExhausted ? -1 : _blockEnum.DocId)
        : (_index >= 0 && _index < _count ? _docIds![_index] : -1);

    /// <summary>Gets the term frequency in the current document. Returns 1 if frequency data is unavailable.</summary>
    public int Freq => _lazyMode ? (_blockEnum.IsExhausted ? 1 : _blockEnum.Freq)
        : (_index >= 0 && _index < _count && _freqs is not null ? _freqs[_index] : 1);

    /// <summary>Gets a value indicating whether the cursor has passed the last document.</summary>
    public bool IsExhausted => _lazyMode ? _blockEnum.IsExhausted : _count == 0;

    private PostingsEnum(int[]? docIds, int[]? freqs, int count,
        int[]? positionData = null, int[]? positionStarts = null)
    {
        _docIds = docIds;
        _freqs = freqs;
        _count = count;
        _index = -1;
        _disposed = false;
        _positionData = positionData;
        _positionStarts = positionStarts;
        _positionByteOffsets = null;
        _positionCounts = null;
        _posBasePtr = null;
        _lazyPosBuffer = null;
        _lastDecodedPosIndex = -1;
        _lastDecodedPosCount = 0;
        _sourceInput = null;
        _hasPayloads = false;
        _payloadByteOffsets = null;
        _payloadLengths = null;
        _blockEnum = default;
        _lazyMode = false;
    }

    private PostingsEnum(int[]? docIds, int[]? freqs, int count,
        long[]? positionByteOffsets, int[]? positionCounts, byte* posBasePtr, IndexInput? sourceInput,
        bool hasPayloads = false)
    {
        _docIds = docIds;
        _freqs = freqs;
        _count = count;
        _index = -1;
        _disposed = false;
        _positionData = null;
        _positionStarts = null;
        _positionByteOffsets = positionByteOffsets;
        _positionCounts = positionCounts;
        _posBasePtr = posBasePtr;
        _lazyPosBuffer = null;
        _lastDecodedPosIndex = -1;
        _lastDecodedPosCount = 0;
        _sourceInput = sourceInput;
        _hasPayloads = hasPayloads;
        _payloadByteOffsets = null;
        _payloadLengths = null;
        _blockEnum = default;
        _lazyMode = false;
    }

    /// <summary>Lazy mode: wraps a BlockPostingsEnum without pre-decoding.</summary>
    private PostingsEnum(BlockPostingsEnum blockEnum)
    {
        _blockEnum = blockEnum;
        _lazyMode = true;
        _docIds = null;
        _freqs = null;
        _count = 0;
        _index = -1;
        _disposed = false;
        _positionData = null;
        _positionStarts = null;
        _positionByteOffsets = null;
        _positionCounts = null;
        _posBasePtr = null;
        _lazyPosBuffer = null;
        _lastDecodedPosIndex = -1;
        _lastDecodedPosCount = 0;
        _sourceInput = null;
        _hasPayloads = false;
        _payloadByteOffsets = null;
        _payloadLengths = null;
    }

    /// <summary>Lazy mode with positions: wraps a BlockPostingsEnum and preloaded position metadata.</summary>
    private PostingsEnum(BlockPostingsEnum blockEnum,
        long[]? positionByteOffsets, int[]? positionCounts,
        byte* posBasePtr, IndexInput? sourceInput, bool hasPayloads)
    {
        _blockEnum = blockEnum;
        _lazyMode = true;
        _docIds = null;
        _freqs = null;
        _count = 0;
        _index = -1;
        _disposed = false;
        _positionData = null;
        _positionStarts = null;
        _positionByteOffsets = positionByteOffsets;
        _positionCounts = positionCounts;
        _posBasePtr = posBasePtr;
        _lazyPosBuffer = null;
        _lastDecodedPosIndex = -1;
        _lastDecodedPosCount = 0;
        _sourceInput = sourceInput;
        _hasPayloads = hasPayloads;
        _payloadByteOffsets = null;
        _payloadLengths = null;
    }

    /// <summary>Creates a PostingsEnum by reading from a memory-mapped IndexInput at the specified offset.</summary>
    public static PostingsEnum Create(IndexInput input, long offset, byte postingsVersion = 2)
    {
        if (postingsVersion >= 3)
            return CreateV3(input, offset);

        long cursor = offset;
        int count = input.ReadInt32(ref cursor);
        if (count <= 0)
            return Empty;

        int skipCount = input.ReadInt32(ref cursor);
        if (skipCount > 0)
            cursor += skipCount * 8L;

        var docIds = ArrayPool<int>.Shared.Rent(count);
        int prev = 0;
        for (int i = 0; i < count; i++)
        {
            prev += input.ReadVarInt(ref cursor);
            docIds[i] = prev;
        }

        bool hasFreqs = input.ReadBoolean(ref cursor);
        int[]? freqs = null;
        if (hasFreqs)
        {
            freqs = ArrayPool<int>.Shared.Rent(count);
            for (int i = 0; i < count; i++)
                freqs[i] = input.ReadVarInt(ref cursor);
        }

        return new PostingsEnum(docIds, freqs, count);
    }

    /// <summary>Lazy v3 block-at-a-time postings — no upfront ArrayPool rental.</summary>
    private static PostingsEnum CreateV3(IndexInput input, long offset)
    {
        long cursor = offset;
        int docFreq = input.ReadInt32(ref cursor);
        long skipOffset = input.ReadInt64(ref cursor);
        input.ReadBoolean(ref cursor); // hasFreqs (always decoded by BlockPostingsEnum)
        input.ReadBoolean(ref cursor); // hasPositions
        input.ReadBoolean(ref cursor); // hasPayloads

        if (docFreq <= 0) return Empty;

        long docStartOffset = cursor;
        var blockEnum = BlockPostingsEnum.Create(input, docStartOffset, skipOffset, docFreq);
        return new PostingsEnum(blockEnum);
    }

    /// <summary>
    /// Creates a PostingsEnum that lazily decodes position data for phrase queries.
    /// During creation, only per-doc byte offsets and position counts are recorded.
    /// Actual position values are decoded on-demand via <see cref="GetCurrentPositions"/>.
    /// </summary>
    public static PostingsEnum CreateWithPositions(IndexInput input, long offset, byte postingsVersion = 2)
    {
        if (postingsVersion >= 3)
            return CreateV3WithPositions(input, offset);

        long cursor = offset;
        int count = input.ReadInt32(ref cursor);
        if (count <= 0)
            return Empty;

        int skipCount = input.ReadInt32(ref cursor);
        if (skipCount > 0)
            cursor += skipCount * 8L;

        var docIds = ArrayPool<int>.Shared.Rent(count);
        int prev = 0;
        for (int i = 0; i < count; i++)
        {
            prev += input.ReadVarInt(ref cursor);
            docIds[i] = prev;
        }

        bool hasFreqs = input.ReadBoolean(ref cursor);
        int[]? freqs = null;
        if (hasFreqs)
        {
            freqs = ArrayPool<int>.Shared.Rent(count);
            for (int i = 0; i < count; i++)
                freqs[i] = input.ReadVarInt(ref cursor);
        }

        bool hasPositions = input.ReadBoolean(ref cursor);

        bool hasPayloads = false;
        if (postingsVersion >= 2)
            hasPayloads = input.ReadBoolean(ref cursor);

        if (!hasPositions)
            return new PostingsEnum(docIds, freqs, count);

        // Record per-doc byte offsets for lazy position decoding
        var positionByteOffsets = RentPosOffsets(count);
        var positionCounts = RentPosCounts(count);

        for (int i = 0; i < count; i++)
        {
            int posCount = input.ReadVarInt(ref cursor);
            positionCounts[i] = posCount;
            positionByteOffsets[i] = cursor;
            // Skip past position VarInts (and inline payloads for v2)
            for (int j = 0; j < posCount; j++)
            {
                input.ReadVarInt(ref cursor); // position delta
                if (hasPayloads)
                {
                    int payloadLen = input.ReadVarInt(ref cursor);
                    if (payloadLen > 0)
                        cursor += payloadLen;
                }
            }
        }

        return new PostingsEnum(docIds, freqs, count, positionByteOffsets, positionCounts, input.BasePointer, input, hasPayloads);
    }

    /// <summary>Eagerly decodes v3 block-packed postings with lazy position data.</summary>
    private static PostingsEnum CreateV3WithPositions(IndexInput input, long offset)
    {
        long cursor = offset;
        int docFreq = input.ReadInt32(ref cursor);
        long skipOffset = input.ReadInt64(ref cursor);
        bool hasFreqs = input.ReadBoolean(ref cursor);
        bool hasPositions = input.ReadBoolean(ref cursor);
        bool hasPayloads = input.ReadBoolean(ref cursor);

        if (docFreq <= 0) return Empty;

        long docStartOffset = cursor;

        // Lazy doc traversal for large postings: avoid renting docIds/freqs arrays.
        // BlockPostingsEnum stays alive for on-demand doc iteration whilst position
        // metadata is preloaded for GetCurrentPositions random access.
        if (docFreq > MaxPositionPreloadDocs && hasPositions)
        {
            var blockEnumLazy = BlockPostingsEnum.Create(input, docStartOffset, skipOffset, docFreq);

            // Position data follows the skip table.
            // Skip table: int32 skipCount (4 bytes) + skipCount × 15 bytes per entry.
            long posCursor = skipOffset;
            int posSkipCount = input.ReadInt32(ref posCursor);
            posCursor += (long)posSkipCount * 15;

            var posOffsets = RentPosOffsets(docFreq);
            var posCounts = RentPosCounts(docFreq);

            for (int i = 0; i < docFreq; i++)
            {
                int posCount = input.ReadVarInt(ref posCursor);
                posCounts[i] = posCount;
                posOffsets[i] = posCursor;
                for (int j = 0; j < posCount; j++)
                {
                    input.ReadVarInt(ref posCursor);
                    if (hasPayloads)
                    {
                        int payloadLen = input.ReadVarInt(ref posCursor);
                        if (payloadLen > 0)
                            posCursor += payloadLen;
                    }
                }
            }

            return new PostingsEnum(blockEnumLazy, posOffsets, posCounts,
                input.BasePointer, input, hasPayloads);
        }

        // Decode doc IDs + freqs via BlockPostingsEnum
        var blockEnum = BlockPostingsEnum.Create(input, docStartOffset, skipOffset, docFreq);
        var docIds = ArrayPool<int>.Shared.Rent(docFreq);
        int[]? freqs = hasFreqs ? ArrayPool<int>.Shared.Rent(docFreq) : null;

        int idx = 0;
        while (blockEnum.NextDoc() != BlockPostingsEnum.NoMoreDocs)
        {
            docIds[idx] = blockEnum.DocId;
            if (freqs != null) freqs[idx] = blockEnum.Freq;
            idx++;
        }

        if (!hasPositions)
            return new PostingsEnum(docIds, freqs, docFreq);

        // Position data follows the skip table (int32 skipCount + skipCount × 15 bytes per entry)
        long posSkipCursor = skipOffset;
        int skipCount = input.ReadInt32(ref posSkipCursor);
        posSkipCursor += (long)skipCount * 15;

        var positionByteOffsets = RentPosOffsets(docFreq);
        var positionCounts = RentPosCounts(docFreq);

        for (int i = 0; i < docFreq; i++)
        {
            int posCount = input.ReadVarInt(ref posSkipCursor);
            positionCounts[i] = posCount;
            positionByteOffsets[i] = posSkipCursor;
            for (int j = 0; j < posCount; j++)
            {
                input.ReadVarInt(ref posSkipCursor); // position delta
                if (hasPayloads)
                {
                    int payloadLen = input.ReadVarInt(ref posSkipCursor);
                    if (payloadLen > 0)
                        posSkipCursor += payloadLen;
                }
            }
        }

        return new PostingsEnum(docIds, freqs, docFreq, positionByteOffsets, positionCounts, input.BasePointer, input, hasPayloads);
    }

    /// <summary>
    /// Returns positions for the current document. Supports both eager (pre-decoded) and
    /// lazy (on-demand) position data. Returns empty span if positions were not available.
    /// Caches the last decoded result to avoid redundant VarInt decoding when called
    /// multiple times for the same document (common in phrase evaluation).
    /// </summary>
    public ReadOnlySpan<int> GetCurrentPositions()
    {
        if (_disposed || _index < 0)
            return ReadOnlySpan<int>.Empty;
        if (_lazyMode ? _blockEnum.IsExhausted : _index >= _count)
            return ReadOnlySpan<int>.Empty;

        // Eager path (pre-decoded positions)
        if (_positionData is not null && _positionStarts is not null)
        {
            int start = _positionStarts[_index];
            int end = _positionStarts[_index + 1];
            return new ReadOnlySpan<int>(_positionData, start, end - start);
        }

        // Lazy path: decode positions on-demand from mmap'd memory
        if (_positionByteOffsets is not null && _positionCounts is not null && _posBasePtr != null)
        {
            int posCount = _positionCounts[_index];
            if (posCount == 0)
                return ReadOnlySpan<int>.Empty;

            // Cache hit: same doc index as last decode — return cached buffer
            if (_index == _lastDecodedPosIndex && _lazyPosBuffer is not null)
                return new ReadOnlySpan<int>(_lazyPosBuffer, 0, _lastDecodedPosCount);

            // Ensure position buffer is large enough
            if (_lazyPosBuffer is null || _lazyPosBuffer.Length < posCount)
            {
                if (_lazyPosBuffer is not null)
                    ReturnLazyPosBuffer(_lazyPosBuffer);
                _lazyPosBuffer = RentLazyPosBuffer(posCount);
            }

            // Prepare payload offset/length buffers for v2
            if (_hasPayloads)
            {
                if (_payloadByteOffsets is null || _payloadByteOffsets.Length < posCount)
                {
                    if (_payloadByteOffsets is not null)
                        ArrayPool<long>.Shared.Return(_payloadByteOffsets);
                    _payloadByteOffsets = ArrayPool<long>.Shared.Rent(posCount);
                }
                if (_payloadLengths is null || _payloadLengths.Length < posCount)
                {
                    if (_payloadLengths is not null)
                        ArrayPool<int>.Shared.Return(_payloadLengths);
                    _payloadLengths = ArrayPool<int>.Shared.Rent(posCount);
                }
            }

            // Decode VarInt position deltas (and payload offsets) directly from mmap'd memory
            long pos = _positionByteOffsets[_index];
            int prevPos = 0;
            for (int j = 0; j < posCount; j++)
            {
                int delta = ReadVarIntFromPtr(_posBasePtr, ref pos);
                prevPos += delta;
                _lazyPosBuffer[j] = prevPos;

                if (_hasPayloads)
                {
                    int payloadLen = ReadVarIntFromPtr(_posBasePtr, ref pos);
                    _payloadLengths![j] = payloadLen;
                    _payloadByteOffsets![j] = pos;
                    pos += payloadLen;
                }
            }

            // Cache this decode result
            _lastDecodedPosIndex = _index;
            _lastDecodedPosCount = posCount;

            return new ReadOnlySpan<int>(_lazyPosBuffer, 0, posCount);
        }

        return ReadOnlySpan<int>.Empty;
    }

    /// <summary>Reads a VarInt directly from a raw byte pointer at the given position.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadVarIntFromPtr(byte* ptr, ref long position)
    {
        uint result = 0;
        int shift = 0;
        byte b;
        do
        {
            b = ptr[position++];
            result |= (uint)(b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return (int)result;
    }

    /// <summary>
    /// Gets the payload for a specific position index of the current document.
    /// Requires <see cref="GetCurrentPositions"/> to have been called first on this document
    /// to populate payload offsets. Returns empty span when no payloads are stored.
    /// </summary>
    public readonly ReadOnlySpan<byte> GetPayload(int positionIndex)
    {
        if (!_hasPayloads || _payloadByteOffsets is null || _payloadLengths is null || _posBasePtr == null)
            return ReadOnlySpan<byte>.Empty;

        if (_index < 0 || _index >= _count)
            return ReadOnlySpan<byte>.Empty;

        int posCount = _positionCounts is not null ? _positionCounts[_index] : 0;
        if ((uint)positionIndex >= (uint)posCount)
            return ReadOnlySpan<byte>.Empty;

        int len = _payloadLengths[positionIndex];
        if (len <= 0)
            return ReadOnlySpan<byte>.Empty;

        return new ReadOnlySpan<byte>(_posBasePtr + _payloadByteOffsets[positionIndex], len);
    }

    /// <summary>Advances the cursor to the next document. Returns <see langword="true"/> if a document was found.</summary>
    /// <returns><see langword="true"/> if there is a next document; <see langword="false"/> if the list is exhausted.</returns>
    public bool MoveNext()
    {
        if (_lazyMode)
        {
            if (_blockEnum.NextDoc() == BlockPostingsEnum.NoMoreDocs)
                return false;
            if (_positionByteOffsets is not null)
                _index = _blockEnum.CurrentGlobalIndex;
            return true;
        }
        if (++_index < _count)
            return true;
        _index = _count;
        return false;
    }

    /// <summary>Resets the cursor to before the first document, allowing the list to be re-iterated.</summary>
    public void Reset() => _index = -1;

    /// <summary>
    /// Advances to the first document >= targetDocId. Returns true if found.
    /// Lazy mode delegates to BlockPostingsEnum skip data for O(log N) seeking.
    /// Eager mode uses binary search on the pre-decoded docId array.
    /// </summary>
    public bool Advance(int targetDocId)
    {
        if (_lazyMode)
        {
            if (_blockEnum.Advance(targetDocId) == BlockPostingsEnum.NoMoreDocs)
                return false;
            if (_positionByteOffsets is not null)
                _index = _blockEnum.CurrentGlobalIndex;
            return true;
        }

        if (_docIds is null || _count == 0) return false;

        int startIndex = Math.Max(0, _index);
        int lo = startIndex, hi = _count - 1;
        int best = _count; // sentinel: not found

        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_docIds[mid] >= targetDocId)
            {
                best = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        if (best < _count)
        {
            _index = best;
            return true;
        }

        _index = _count;
        return false;
    }

    /// <summary>Returns all rented buffers back to <see cref="System.Buffers.ArrayPool{T}"/>.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_lazyMode)
        {
            _blockEnum.Dispose();
            if (_positionByteOffsets is not null)
            {
                ReturnPosOffsets(_positionByteOffsets);
                _positionByteOffsets = null;
            }
            if (_positionCounts is not null)
            {
                ReturnPosCounts(_positionCounts);
                _positionCounts = null;
            }
            if (_lazyPosBuffer is not null)
            {
                ReturnLazyPosBuffer(_lazyPosBuffer);
                _lazyPosBuffer = null;
            }
            return;
        }

        if (_docIds is not null)
        {
            ArrayPool<int>.Shared.Return(_docIds);
            _docIds = null;
        }
        if (_freqs is not null)
        {
            ArrayPool<int>.Shared.Return(_freqs);
            _freqs = null;
        }
        if (_positionData is not null)
        {
            ArrayPool<int>.Shared.Return(_positionData);
            _positionData = null;
        }
        if (_positionStarts is not null)
        {
            ArrayPool<int>.Shared.Return(_positionStarts);
            _positionStarts = null;
        }
        if (_positionByteOffsets is not null)
        {
            ReturnPosOffsets(_positionByteOffsets);
            _positionByteOffsets = null;
        }
        if (_positionCounts is not null)
        {
            ReturnPosCounts(_positionCounts);
            _positionCounts = null;
        }
        if (_lazyPosBuffer is not null)
        {
            ReturnLazyPosBuffer(_lazyPosBuffer);
            _lazyPosBuffer = null;
        }
        if (_payloadByteOffsets is not null)
        {
            ArrayPool<long>.Shared.Return(_payloadByteOffsets);
            _payloadByteOffsets = null;
        }
        if (_payloadLengths is not null)
        {
            ArrayPool<int>.Shared.Return(_payloadLengths);
            _payloadLengths = null;
        }
    }

    /// <summary>A pre-built empty postings enum that is immediately exhausted.</summary>
    public static PostingsEnum Empty => new(null, null, 0);

    /// <summary>
    /// Validates the postings file header. Returns the format version.
    /// Should be called when opening a segment, before using Create/CreateWithPositions.
    /// </summary>
    public static byte ValidateFileHeader(IndexInput input)
    {
        input.Seek(0);
        return CodecConstants.ReadHeaderVersion(input, CodecConstants.PostingsVersion, "postings (.pos)");
    }
}

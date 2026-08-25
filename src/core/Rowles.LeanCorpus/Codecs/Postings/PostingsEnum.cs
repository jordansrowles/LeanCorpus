using System.Buffers;
using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Postings;

/// <summary>
/// Forward-only cursor over a postings list. Decodes doc IDs and frequencies
/// once into ArrayPool-rented buffers, then yields (DocId, Freq) pairs via MoveNext().
/// Optionally decodes positions when created via <see cref="CreateWithPositions"/>.
/// 
/// <para><b>Lifetime contract:</b> When using the lazy position path, this struct holds a raw
/// <c>byte*</c> pointer into a memory-mapped <see cref="IndexInput"/>. The cursor retains the
/// input mapping until the cursor is disposed, so disposing the source input or its owning
/// searcher waits for active cursor operations to finish.</para>
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

    // Lazy block-at-a-time mode: delegates to BlockPostingsEnum
    // instead of pre-decoding all doc IDs/frequencies into ArrayPool arrays.
    private BlockPostingsEnum _blockEnum;
    private readonly bool _lazyMode;

    // Lazy position decoding: store per-doc byte offsets and base pointer
    private long[]? _positionByteOffsets;
    private int[]? _positionCounts;
    private byte* _posBasePtr;
    private int[]? _lazyPosBuffer;
    private int _lastDecodedPosIndex; // cache: skip re-decode if same doc
    private int _lastDecodedPosCount; // number of valid positions in _lazyPosBuffer
    // Prevents the IndexInput from being disposed/GC'd while this PostingsEnum holds a raw pointer
    private readonly IndexInput? _sourceInput;
    private readonly long _posDataEnd; // exclusive byte offset bound for _posBasePtr reads

    // Payload support
    private readonly bool _hasPayloads;
    private long[]? _payloadByteOffsets;
    private int[]? _payloadLengths;

    // Shared disposal guard: all copies of this struct reference the same guard.
    // When any copy is disposed, the guard is marked and all other copies detect it.
    private readonly DisposalGuard? _guard;

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
    public int DocId
    {
        get
        {
            if (!TryEnterLease())
                return -1;
            try
            {
                return _lazyMode ? _blockEnum.DocId
                    : (_index >= 0 && _index < _count ? _docIds![_index] : -1);
            }
            finally
            {
                ExitLease();
            }
        }
    }

    public int Freq
    {
        get
        {
            if (!TryEnterLease())
                return 1;
            try
            {
                return _lazyMode ? (_blockEnum.IsExhausted ? 1 : _blockEnum.Freq)
                    : (_index >= 0 && _index < _count && _freqs is not null ? _freqs[_index] : 1);
            }
            finally
            {
                ExitLease();
            }
        }
    }

    /// <summary>Gets a value indicating whether the cursor has passed the last document.</summary>
    public bool IsExhausted => _lazyMode ? _blockEnum.IsExhausted : _index >= _count;

    /// <summary>
    /// Gets the underlying BlockPostingsEnum, or throws if the enum was
    /// eagerly decoded (in which case block-level metadata isn't available).
    /// </summary>
    internal BlockPostingsEnum BlockEnum =>
        _lazyMode ? _blockEnum : throw new InvalidOperationException(
            "Block-level metadata is not available for eagerly-decoded postings.");

    /// <summary>
    /// Returns true if this postings enum was created in lazy mode and supports
    /// block-level metadata access via <see cref="BlockEnum"/>.
    /// </summary>
    internal bool HasBlockMetadata => _lazyMode;

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
        _posDataEnd = 0;
        _hasPayloads = false;
        _payloadByteOffsets = null;
        _payloadLengths = null;
        _blockEnum = default;
        _lazyMode = false;
        _guard = new DisposalGuard();
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
        _posDataEnd = sourceInput?.Length ?? 0;
        _hasPayloads = hasPayloads;
        _payloadByteOffsets = null;
        _payloadLengths = null;
        _blockEnum = default;
        _lazyMode = false;
        _guard = new DisposalGuard();
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
        _posDataEnd = 0;
        _hasPayloads = false;
        _payloadByteOffsets = null;
        _payloadLengths = null;
        _guard = new DisposalGuard();
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
        _posDataEnd = sourceInput?.Length ?? 0;
        _hasPayloads = hasPayloads;
        _payloadByteOffsets = null;
        _payloadLengths = null;
        _guard = new DisposalGuard();
    }

    /// <summary>Creates a PostingsEnum by reading from a memory-mapped IndexInput at the specified offset.</summary>
    /// <remarks>Use <see cref="CreateWithPositions"/> when the caller needs positions or payloads.</remarks>
    public static PostingsEnum Create(IndexInput input, long offset)
    {
        var inputLease = input.AcquireLifetimeLease();
        try
        {
            var postings = CreateCore(input, offset);
            if (postings.DocFreq == 0)
                inputLease.Dispose();
            else
                postings.AttachInputLifetimeLease(inputLease);
            return postings;
        }
        catch
        {
            inputLease.Dispose();
            throw;
        }
    }

    private static PostingsEnum CreateCore(IndexInput input, long offset)
    {
        ReadTermMetadata(input, offset, out long docStartOffset, out int docFreq,
            out long skipOffset, out _, out _, out _);

        if (docFreq <= 0) return Empty;

        var blockEnum = BlockPostingsEnum.Create(input, docStartOffset, skipOffset, docFreq);
        return new PostingsEnum(blockEnum);
    }

    /// <summary>
    /// Creates a PostingsEnum that lazily decodes position data for phrase queries.
    /// During creation, only per-doc byte offsets and position counts are recorded.
    /// Actual position values are decoded on-demand via <see cref="GetCurrentPositions"/>.
    /// </summary>
    public static PostingsEnum CreateWithPositions(IndexInput input, long offset)
    {
        var inputLease = input.AcquireLifetimeLease();
        try
        {
            var postings = CreateWithPositionsCore(input, offset);
            if (postings.DocFreq == 0)
                inputLease.Dispose();
            else
                postings.AttachInputLifetimeLease(inputLease);
            return postings;
        }
        catch
        {
            inputLease.Dispose();
            throw;
        }
    }

    private static PostingsEnum CreateWithPositionsCore(IndexInput input, long offset)
    {
        ReadTermMetadata(input, offset, out long docStartOffset, out int docFreq,
            out long skipOffset, out bool hasFreqs, out bool hasPositions, out bool hasPayloads);

        if (docFreq <= 0) return Empty;

        if (docFreq > MaxPositionPreloadDocs && hasPositions)
        {
            var blockEnumLazy = BlockPostingsEnum.Create(input, docStartOffset, skipOffset, docFreq);

            long posCursor = skipOffset;
            using var reader = input.BeginReadSession();
            int posSkipCount = reader.ReadInt32(ref posCursor);
            posCursor += (long)posSkipCount * 15;

            var posOffsets = RentPosOffsets(docFreq);
            var posCounts = RentPosCounts(docFreq);

            for (int i = 0; i < docFreq; i++)
            {
                int posCount = reader.ReadVarInt(ref posCursor);
                posCounts[i] = posCount;
                posOffsets[i] = posCursor;
                for (int j = 0; j < posCount; j++)
                {
                    reader.ReadVarInt(ref posCursor);
                    if (hasPayloads)
                    {
                        int payloadLen = reader.ReadVarInt(ref posCursor);
                        if (payloadLen > 0)
                            posCursor += payloadLen;
                    }
                }
            }

            return new PostingsEnum(blockEnumLazy, posOffsets, posCounts,
                input.BasePointer, input, hasPayloads);
        }

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

        long posSkipCursor = skipOffset;
        using var positionReader = input.BeginReadSession();
        int skipCount = positionReader.ReadInt32(ref posSkipCursor);
        posSkipCursor += (long)skipCount * 15;

        var positionByteOffsets = RentPosOffsets(docFreq);
        var positionCounts = RentPosCounts(docFreq);

        for (int i = 0; i < docFreq; i++)
        {
            int posCount = positionReader.ReadVarInt(ref posSkipCursor);
            positionCounts[i] = posCount;
            positionByteOffsets[i] = posSkipCursor;
            for (int j = 0; j < posCount; j++)
            {
                positionReader.ReadVarInt(ref posSkipCursor);
                if (hasPayloads)
                {
                    int payloadLen = positionReader.ReadVarInt(ref posSkipCursor);
                    if (payloadLen > 0)
                        posSkipCursor += payloadLen;
                }
            }
        }

        return new PostingsEnum(docIds, freqs, docFreq, positionByteOffsets, positionCounts, input.BasePointer, input, hasPayloads);
    }

    internal static void ReadTermMetadata(IndexInput input, long offset, out long docStartOffset,
        out int docFreq, out long skipOffset, out bool hasFreqs, out bool hasPositions,
        out bool hasPayloads)
    {
        using var reader = input.BeginReadSession();
        long versionCursor = 0;
        byte version = reader.ReadByte(ref versionCursor);
        long cursor = offset;
        docStartOffset = version >= PostingsFileHeader.V4 ? reader.ReadInt64(ref cursor) : cursor;
        docFreq = reader.ReadInt32(ref cursor);
        skipOffset = reader.ReadInt64(ref cursor);
        hasFreqs = reader.ReadByte(ref cursor) != 0;
        hasPositions = reader.ReadByte(ref cursor) != 0;
        hasPayloads = reader.ReadByte(ref cursor) != 0;
        if (version < PostingsFileHeader.V4)
        {
            docStartOffset = cursor;
            if (docFreq <= 0 || skipOffset < docStartOffset || skipOffset >= input.Length)
            {
                // Migration tests and interrupted upgrades may expose v4-shaped data
                // whose version byte still identifies the previous layout.
                cursor = offset;
                docStartOffset = reader.ReadInt64(ref cursor);
                docFreq = reader.ReadInt32(ref cursor);
                skipOffset = reader.ReadInt64(ref cursor);
                hasFreqs = reader.ReadByte(ref cursor) != 0;
                hasPositions = reader.ReadByte(ref cursor) != 0;
                hasPayloads = reader.ReadByte(ref cursor) != 0;
            }
        }
    }

    /// <summary>
    /// Returns positions for the current document. Supports both eager (pre-decoded) and
    /// lazy (on-demand) position data. Returns empty span if positions were not available.
    /// Caches the last decoded result to avoid redundant VarInt decoding when called
    /// multiple times for the same document (common in phrase evaluation).
    /// </summary>
    public ReadOnlySpan<int> GetCurrentPositions()
    {
        if (!TryEnterLease())
            return ReadOnlySpan<int>.Empty;
        try
        {
            return GetCurrentPositionsUnchecked();
        }
        finally
        {
            ExitLease();
        }
    }

    /// <summary>Returns positions without entering the shared disposal guard.</summary>
    /// <remarks>
    /// Internal search loops may use this only while the cursor's containing lifetime
    /// remains pinned for the complete operation.
    /// </remarks>
    internal ReadOnlySpan<int> GetCurrentPositionsUnchecked()
    {
        if (_disposed || _index < 0 || (_lazyMode ? _blockEnum.IsExhausted : _index >= _count))
            return ReadOnlySpan<int>.Empty;

        if (_positionData is not null && _positionStarts is not null)
        {
            int start = _positionStarts[_index];
            int end = _positionStarts[_index + 1];
            return new ReadOnlySpan<int>(_positionData, start, end - start);
        }

        if (_positionByteOffsets is null || _positionCounts is null || _posBasePtr == null)
            return ReadOnlySpan<int>.Empty;

        int posCount = _positionCounts[_index];
        if (posCount == 0)
            return ReadOnlySpan<int>.Empty;

        if (_index == _lastDecodedPosIndex && _lazyPosBuffer is not null)
            return new ReadOnlySpan<int>(_lazyPosBuffer, 0, _lastDecodedPosCount);

        if (_lazyPosBuffer is null || _lazyPosBuffer.Length < posCount)
        {
            if (_lazyPosBuffer is not null)
                ReturnLazyPosBuffer(_lazyPosBuffer);
            _lazyPosBuffer = RentLazyPosBuffer(posCount);
        }

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

        long pos = _positionByteOffsets[_index];
        if ((ulong)pos >= (ulong)_posDataEnd)
            return ReadOnlySpan<int>.Empty;

        int previousPosition = 0;
        for (int i = 0; i < posCount; i++)
        {
            if (!TryReadVarIntFromPtr(_posBasePtr, ref pos, _posDataEnd, out int delta))
                return ReadOnlySpan<int>.Empty;
            previousPosition += delta;
            _lazyPosBuffer[i] = previousPosition;

            if (_hasPayloads)
            {
                if (!TryReadVarIntFromPtr(_posBasePtr, ref pos, _posDataEnd, out int payloadLength))
                    return ReadOnlySpan<int>.Empty;
                _payloadLengths![i] = payloadLength;
                _payloadByteOffsets![i] = pos;
                long nextPosition = pos + (long)(uint)payloadLength;
                if (nextPosition > _posDataEnd || nextPosition < pos)
                    return ReadOnlySpan<int>.Empty;
                pos = nextPosition;
            }
        }

        _lastDecodedPosIndex = _index;
        _lastDecodedPosCount = posCount;
        return new ReadOnlySpan<int>(_lazyPosBuffer, 0, posCount);
    }

    /// <summary>Reads a VarInt from a raw byte pointer with bounds checking.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadVarIntFromPtr(byte* ptr, ref long position, long end, out int value)
    {
        value = 0;
        uint result = 0;
        int shift = 0;
        int remaining = 0;
        byte b;
        do
        {
            if (position >= end) return false;
            b = ptr[position++];
            remaining++;
            if (remaining > 5) return false; // VarInt for int32 max 5 bytes
            result |= (uint)(b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        if (result > int.MaxValue) return false;
        value = (int)result;
        return true;
    }

    /// <summary>
    /// Gets the payload for a specific position index of the current document.
    /// Requires <see cref="GetCurrentPositions"/> to have been called first on this document
    /// to populate payload offsets. Returns empty span when no payloads are stored.
    /// </summary>
    public readonly ReadOnlySpan<byte> GetPayload(int positionIndex)
    {
        if (!TryEnterLease())
            return ReadOnlySpan<byte>.Empty;
        if (_disposed)
        {
            ExitLease();
            return ReadOnlySpan<byte>.Empty;
        }
        try
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

        long offset = _payloadByteOffsets[positionIndex];
        if ((ulong)offset + (ulong)(uint)len > (ulong)_posDataEnd)
            return ReadOnlySpan<byte>.Empty;

            return new ReadOnlySpan<byte>(_posBasePtr + offset, len);
        }
        finally
        {
            ExitLease();
        }
    }

    /// <summary>Advances the cursor to the next document. Returns <see langword="true"/> if a document was found.</summary>
    /// <returns><see langword="true"/> if there is a next document; <see langword="false"/> if the list is exhausted.</returns>
    public bool MoveNext()
    {
        if (!TryEnterLease())
            throw new ObjectDisposedException(nameof(PostingsEnum), "This PostingsEnum is a copy of an already-disposed instance.");
        try
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
        finally
        {
            ExitLease();
        }
    }

    /// <summary>
    /// Advances the cursor and returns the current document ID and frequency.
    /// </summary>
    /// <remarks>
    /// Prefer this overload when both values are required. It performs one shared
    /// lifetime-guard operation for the complete cursor step.
    /// </remarks>
    public bool MoveNext(out int docId, out int frequency)
    {
        if (!TryEnterLease())
            throw new ObjectDisposedException(nameof(PostingsEnum), "This PostingsEnum is a copy of an already-disposed instance.");
        try
        {
            return MoveNextUnchecked(out docId, out frequency);
        }
        finally
        {
            ExitLease();
        }
    }

    /// <summary>
    /// Advances without entering the shared disposal guard.
    /// </summary>
    /// <remarks>
    /// Internal search loops may use this only while a segment query or read lease
    /// pins the cursor's backing state for the complete loop.
    /// </remarks>
    internal bool MoveNextUnchecked(out int docId, out int frequency)
    {
        if (_lazyMode)
        {
            if (_blockEnum.NextDoc() == BlockPostingsEnum.NoMoreDocs)
            {
                docId = -1;
                frequency = 1;
                return false;
            }

            if (_positionByteOffsets is not null)
                _index = _blockEnum.CurrentGlobalIndex;
            docId = _blockEnum.DocId;
            frequency = _blockEnum.Freq;
            return true;
        }

        if (++_index < _count)
        {
            docId = _docIds![_index];
            frequency = _freqs is not null ? _freqs[_index] : 1;
            return true;
        }

        _index = _count;
        docId = -1;
        frequency = 1;
        return false;
    }

    public void Reset()
    {
        if (!TryEnterLease())
            return;
        try
        {
            if (_lazyMode) _blockEnum.Reset(); _index = -1;
        }
        finally
        {
            ExitLease();
        }
    }

    /// <summary>
    /// Advances to the first document >= targetDocId. Returns true if found.
    /// Lazy mode delegates to BlockPostingsEnum skip data for O(log N) seeking.
    /// Eager mode uses binary search on the pre-decoded docId array.
    /// </summary>
    public bool Advance(int targetDocId)
    {
        if (!TryEnterLease())
            return false;
        try
        {
            return AdvanceUnchecked(targetDocId, out _, out _);
        }
        finally
        {
            ExitLease();
        }
    }

    /// <summary>Advances without entering the shared disposal guard.</summary>
    /// <remarks>
    /// Internal search loops may use this only while the cursor's containing lifetime
    /// remains pinned for the complete operation.
    /// </remarks>
    internal bool AdvanceUnchecked(int targetDocId, out int docId, out int frequency)
    {
        if (_lazyMode)
        {
            if (_blockEnum.Advance(targetDocId) == BlockPostingsEnum.NoMoreDocs)
            {
                docId = -1;
                frequency = 1;
                return false;
            }
            if (_positionByteOffsets is not null)
                _index = _blockEnum.CurrentGlobalIndex;
            docId = _blockEnum.DocId;
            frequency = _blockEnum.Freq;
            return true;
        }

        if (_docIds is null || _count == 0)
        {
            docId = -1;
            frequency = 1;
            return false;
        }

        int lo = Math.Max(0, _index);
        int hi = _count - 1;
        int best = _count;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
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
            docId = _docIds[best];
            frequency = _freqs is not null ? _freqs[best] : 1;
            return true;
        }

        _index = _count;
        docId = -1;
        frequency = 1;
        return false;
    }

    /// <summary>Returns all rented buffers back to <see cref="System.Buffers.ArrayPool{T}"/>.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_guard is not null && !_guard.BeginDispose())
            return;

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
            _guard?.ReleaseReaderLease();
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
        _guard?.ReleaseReaderLease();
    }

    /// <summary>A pre-built empty postings enum that is immediately exhausted.</summary>
    public static PostingsEnum Empty => new(null, null, 0);

    /// <summary>Transfers a segment-state lease to this cursor's shared disposal guard.</summary>
    internal readonly void AttachLifetimeLease(LifetimeLease stateLease)
        => _guard?.AttachLifetimeLease(stateLease);

    private readonly void AttachInputLifetimeLease(IndexInputLifetimeLease lease)
        => _guard?.AttachInputLifetimeLease(lease);

    private readonly bool TryEnterLease() => _guard?.TryEnter() ?? true;

    private readonly void ExitLease() => _guard?.Exit();

    /// <summary>
    /// Shared lifetime lease referenced by every copy of a <see cref="PostingsEnum"/>.
    /// It grants exactly one disposer ownership of the rented buffers and waits for any
    /// in-progress enum operation before those buffers are returned to their pools.
    /// </summary>
    private sealed class DisposalGuard
    {
        private const int DisposeRequested = int.MinValue;
        private int _state;
        private LifetimeLease _lifetimeLease;
        private bool _hasLifetimeLease;
        private IndexInputLifetimeLease _inputLifetimeLease;
        private bool _hasInputLifetimeLease;

        public void AttachLifetimeLease(LifetimeLease stateLease)
        {
            _lifetimeLease = stateLease;
            _hasLifetimeLease = true;
        }

        public void AttachInputLifetimeLease(IndexInputLifetimeLease lease)
        {
            _inputLifetimeLease = lease;
            _hasInputLifetimeLease = true;
        }

        public void ReleaseReaderLease()
        {
            // Release the mapping lease before the cache lease. Releasing the cache
            // lease can synchronously evict and dispose the segment state, whose
            // IndexInput.Dispose() must not wait on this cursor's own mapping lease.
            if (_hasInputLifetimeLease)
            {
                _hasInputLifetimeLease = false;
                _inputLifetimeLease.Dispose();
            }
            if (_hasLifetimeLease)
            {
                _hasLifetimeLease = false;
                _lifetimeLease.Dispose();
            }
        }

        public bool TryEnter()
        {
            while (true)
            {
                int state = Volatile.Read(ref _state);
                if (state < 0)
                    return false;

                if (Interlocked.CompareExchange(ref _state, state + 1, state) == state)
                    return true;
            }
        }

        public void Exit() => Interlocked.Decrement(ref _state);

        public bool BeginDispose()
        {
            while (true)
            {
                int state = Volatile.Read(ref _state);
                if (state < 0)
                    return false;

                if (Interlocked.CompareExchange(ref _state, state | DisposeRequested, state) != state)
                    continue;

                var spinner = new SpinWait();
                while (Volatile.Read(ref _state) != DisposeRequested)
                    spinner.SpinOnce();
                return true;
            }
        }
    }

    /// <summary>
    /// Validates the postings file header. Returns the format version.
    /// Should be called when opening a segment, before using Create/CreateWithPositions.
    /// </summary>
    public static byte ValidateFileHeader(IndexInput input)
    {
        input.Seek(0);
        byte version = PostingsFileHeader.ReadVersion(input);
        if (version > CodecConstants.PostingsVersion)
            throw new InvalidDataException(
                $"Unsupported postings (.pos) format version {version}. " +
                $"This build supports up to version {CodecConstants.PostingsVersion}. " +
                "Please upgrade LeanCorpus.");
        return version;
    }
}

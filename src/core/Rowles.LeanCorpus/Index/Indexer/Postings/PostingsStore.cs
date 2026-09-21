using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;

namespace Rowles.LeanCorpus.Index.Indexer.Postings;

/// <summary>
/// Owns the compact postings state for one live DWPT or detached snapshot.
/// </summary>
internal sealed class PostingsStore : IDisposable
{
    private const int InitialTermCapacity = 256;
    private const int StackQualifiedTermLimit = 256;

    private readonly ArrayPool<PostingTermState> _statePool;
    private readonly ArrayPool<byte> _bytePool;
    private readonly bool _storePayloads;
    private readonly bool _storeTermVectors;
    private readonly Dictionary<string, int> _fieldOrdinals = new(StringComparer.Ordinal);
    private readonly List<string> _fieldNames = [];
    private readonly List<byte[]> _fieldPrefixesUtf8 = [];
    private PostingTermState[] _states;
    private readonly BytesRefHash _termHash;
    private readonly PostingsByteArena _arena;
    private long _allocatedBytes;
    private long _fieldPrefixBytes;
    private int _accountingDirty;
    private int _frozen;
    private int _disposed;

    internal PostingsStore(bool storePayloads = false, bool storeTermVectors = false)
        : this(storePayloads, storeTermVectors, ArrayPool<PostingTermState>.Shared, ArrayPool<byte>.Shared)
    {
    }

    internal PostingsStore(
        bool storePayloads,
        bool storeTermVectors,
        ArrayPool<PostingTermState> statePool,
        ArrayPool<byte> bytePool)
    {
        ArgumentNullException.ThrowIfNull(statePool);
        ArgumentNullException.ThrowIfNull(bytePool);
        _statePool = statePool;
        _bytePool = bytePool;
        _storePayloads = storePayloads;
        _storeTermVectors = storeTermVectors;
        _termHash = new BytesRefHash(InitialTermCapacity, bytePool);
        _states = _statePool.Rent(InitialTermCapacity);
        _arena = new PostingsByteArena(bytePool);
        RefreshAllocatedBytes();
    }

    internal int TermCount => _termHash.Count;
    internal int TermStateCapacity => _states.Length;
    internal BytesRefHash TermHash => _termHash;
    internal PostingsByteArena Arena => _arena;

    internal long AllocatedBytes => Volatile.Read(ref _allocatedBytes);

    internal void AddDocOnly(string fieldName, ReadOnlySpan<char> term, int docId)
    {
        int termHashVersion = _termHash.AllocationVersion;
        int arenaVersion = _arena.AllocationVersion;
        try
        {
            AddCore(fieldName, term, docId, 0, FieldIndexOptions.DocsOnly, payload: null, 0, 0);
        }
        finally
        {
            RefreshAllocatedBytesIfCapacityChanged(termHashVersion, arenaVersion);
        }
    }

    internal void Add(
        string fieldName,
        ReadOnlySpan<char> term,
        int docId,
        int position,
        FieldIndexOptions indexOptions,
        byte[]? payload,
        int startOffset,
        int endOffset)
    {
        int termHashVersion = _termHash.AllocationVersion;
        int arenaVersion = _arena.AllocationVersion;
        try
        {
            AddCore(fieldName, term, docId, position, indexOptions, payload, startOffset, endOffset);
        }
        finally
        {
            RefreshAllocatedBytesIfCapacityChanged(termHashVersion, arenaVersion);
        }
    }

    internal void Freeze()
    {
        ThrowIfDisposed();
        Interlocked.Exchange(ref _frozen, 1);
    }

    internal string GetFieldName(int ordinal)
    {
        ThrowIfDisposed();
        if ((uint)ordinal >= (uint)_fieldNames.Count)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        return _fieldNames[ordinal];
    }

    internal ReadOnlySpan<byte> GetFieldPrefixUtf8(int ordinal)
    {
        ThrowIfDisposed();
        if ((uint)ordinal >= (uint)_fieldPrefixesUtf8.Count)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        return _fieldPrefixesUtf8[ordinal];
    }

    internal ReadOnlySpan<byte> GetTerm(int termId) => _termHash.GetTerm(termId);

    internal ref readonly PostingTermState GetTermState(int termId)
    {
        ThrowIfDisposed();
        if ((uint)termId >= (uint)_termHash.Count)
            throw new ArgumentOutOfRangeException(nameof(termId));
        return ref _states[termId];
    }

    internal PostingDocReader OpenDocReader(int termId)
    {
        ref readonly PostingTermState state = ref GetTermState(termId);
        return new PostingDocReader(_arena, in state);
    }

    internal PostingProxReader OpenProxReader(int termId)
    {
        ref readonly PostingTermState state = ref GetTermState(termId);
        return new PostingProxReader(_arena, in state, _storePayloads, _storeTermVectors);
    }

    internal bool StorePayloads => _storePayloads;
    internal bool StoreTermVectors => _storeTermVectors;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _arena.Dispose();
        _termHash.Dispose();
        if (_states.Length > 0)
            _statePool.Return(_states, clearArray: false);
        _states = [];
        _fieldOrdinals.Clear();
        _fieldNames.Clear();
        _fieldPrefixesUtf8.Clear();
        _fieldPrefixBytes = 0;
        Volatile.Write(ref _allocatedBytes, 0);
        Interlocked.Exchange(ref _frozen, 1);
    }

    private void AddCore(
        string fieldName,
        ReadOnlySpan<char> term,
        int docId,
        int position,
        FieldIndexOptions indexOptions,
        byte[]? payload,
        int startOffset,
        int endOffset)
    {
        ThrowIfMutable();
        ArgumentNullException.ThrowIfNull(fieldName);
        if (docId < 0)
            throw new ArgumentOutOfRangeException(nameof(docId));
        if ((int)indexOptions is < (int)FieldIndexOptions.DocsOnly or > (int)FieldIndexOptions.DocsAndFreqsAndPositionsAndOffsets)
            throw new ArgumentOutOfRangeException(nameof(indexOptions));

        int fieldOrdinal = GetOrCreateFieldOrdinal(fieldName);
        int termId = GetOrCreateTermId(fieldOrdinal, term);
        ref PostingTermState state = ref _states[termId];

        bool hasFreqs = indexOptions >= FieldIndexOptions.DocsAndFreqs;
        bool hasPositions = indexOptions >= FieldIndexOptions.DocsAndFreqsAndPositions;
        bool hasOffsets = indexOptions >= FieldIndexOptions.DocsAndFreqsAndPositionsAndOffsets;
        if (hasFreqs)
            state.Flags |= PostingFlags.HasFreqs;
        if (hasPositions)
            state.Flags |= PostingFlags.HasPositions;
        if (hasOffsets && _storeTermVectors)
            state.Flags |= PostingFlags.HasOffsets;

        if (state.CurrentDocId < 0)
        {
            state.CurrentDocId = docId;
            state.CurrentFreq = hasFreqs ? 1 : 0;
            state.CurrentPositionCount = 0;
            state.LastPosition = 0;
            state.DocFreq = 1;
        }
        else if (docId < state.CurrentDocId)
        {
            throw new InvalidOperationException(
                $"Posting document IDs must be non-decreasing, but {docId} followed {state.CurrentDocId}.");
        }
        else if (docId != state.CurrentDocId)
        {
            FlushCurrentDocument(ref state);
            state.CurrentDocId = docId;
            state.CurrentFreq = hasFreqs ? 1 : 0;
            state.CurrentPositionCount = 0;
            state.LastPosition = 0;
            state.DocFreq = checked(state.DocFreq + 1);
        }
        else if (hasFreqs)
        {
            state.CurrentFreq = state.CurrentFreq == 0 ? 1 : checked(state.CurrentFreq + 1);
        }

        if (!hasPositions)
            return;

        if (position < 0)
            throw new ArgumentOutOfRangeException(nameof(position));
        if (position < state.LastPosition)
            throw new InvalidOperationException(
                $"Posting positions must be non-decreasing, but {position} followed {state.LastPosition}.");
        if (startOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(startOffset));
        if (endOffset < startOffset)
            throw new ArgumentOutOfRangeException(nameof(endOffset));

        if (state.ProxStreamCursor.StartAddress == 0)
            state.ProxStreamCursor = _arena.StartStream();

        _arena.WriteVarUInt(ref state.ProxStreamCursor, checked((uint)(position - state.LastPosition)));
        state.LastPosition = position;
        state.CurrentPositionCount++;

        int payloadLength = 0;
        if (_storePayloads)
        {
            payloadLength = payload?.Length ?? 0;
            _arena.WriteVarUInt(ref state.ProxStreamCursor, checked((uint)payloadLength));
        }

        if (_storeTermVectors)
        {
            _arena.WriteVarUInt(ref state.ProxStreamCursor, hasOffsets ? 1u : 0u);
            if (hasOffsets)
            {
                _arena.WriteVarUInt(ref state.ProxStreamCursor, checked((uint)startOffset));
                _arena.WriteVarUInt(ref state.ProxStreamCursor, checked((uint)endOffset));
            }
        }

        if (payloadLength > 0)
        {
            state.Flags |= PostingFlags.HasPayloads;
            _arena.WriteBytes(ref state.ProxStreamCursor, payload);
        }
    }

    private void FlushCurrentDocument(ref PostingTermState state)
    {
        if (state.CurrentDocId < 0)
            return;
        if (state.CurrentDocId < state.LastFlushedDocId)
            throw new InvalidOperationException("Posting document IDs moved backwards.");

        if (state.DocStreamCursor.StartAddress == 0)
            state.DocStreamCursor = _arena.StartStream();

        int delta = state.LastFlushedDocId < 0
            ? state.CurrentDocId
            : state.CurrentDocId - state.LastFlushedDocId;
        if (delta < 0)
            throw new InvalidOperationException("Posting document delta became negative.");

        _arena.WriteVarUInt(ref state.DocStreamCursor, checked((uint)delta));
        _arena.WriteVarUInt(ref state.DocStreamCursor, checked((uint)state.CurrentFreq));
        _arena.WriteVarUInt(ref state.DocStreamCursor, checked((uint)state.CurrentPositionCount));
        state.LastFlushedDocId = state.CurrentDocId;
    }

    private int GetOrCreateTermId(int fieldOrdinal, ReadOnlySpan<char> term)
    {
        ReadOnlySpan<byte> prefix = GetFieldPrefixUtf8(fieldOrdinal);
        int maxTermBytes = Encoding.UTF8.GetMaxByteCount(term.Length);
        long maxTotal = (long)prefix.Length + maxTermBytes;
        if (maxTotal > int.MaxValue)
            throw new InvalidOperationException("The qualified term is too large.");

        if (maxTotal <= StackQualifiedTermLimit)
        {
            Span<byte> qualified = stackalloc byte[(int)maxTotal];
            prefix.CopyTo(qualified);
            int termBytes = Encoding.UTF8.GetBytes(term, qualified[prefix.Length..]);
            int id = _termHash.Add(qualified[..(prefix.Length + termBytes)]);
            if (id >= 0)
            {
                EnsureStateCapacity(id + 1);
                _states[id].Initialise(fieldOrdinal);
            }
            return id >= 0 ? id : -(id + 1);
        }

        byte[] rented = _bytePool.Rent((int)maxTotal);
        try
        {
            Span<byte> qualified = rented;
            prefix.CopyTo(qualified);
            int termBytes = Encoding.UTF8.GetBytes(term, qualified[prefix.Length..]);
            int id = _termHash.Add(qualified[..(prefix.Length + termBytes)]);
            if (id >= 0)
            {
                EnsureStateCapacity(id + 1);
                _states[id].Initialise(fieldOrdinal);
            }
            return id >= 0 ? id : -(id + 1);
        }
        finally
        {
            _bytePool.Return(rented, clearArray: false);
        }
    }

    private int GetOrCreateFieldOrdinal(string fieldName)
    {
        if (_fieldOrdinals.TryGetValue(fieldName, out int ordinal))
            return ordinal;

        ordinal = _fieldNames.Count;
        int byteCount = Encoding.UTF8.GetByteCount(fieldName);
        byte[] prefix = new byte[checked(byteCount + 1)];
        Encoding.UTF8.GetBytes(fieldName.AsSpan(), prefix.AsSpan(0, byteCount));
        prefix[byteCount] = 0;
        _fieldOrdinals.Add(fieldName, ordinal);
        _fieldNames.Add(fieldName);
        _fieldPrefixesUtf8.Add(prefix);
        _fieldPrefixBytes = checked(_fieldPrefixBytes + prefix.LongLength);
        Volatile.Write(ref _accountingDirty, 1);
        return ordinal;
    }

    private void EnsureStateCapacity(int required)
    {
        if (required <= _states.Length)
            return;

        int newSize = Math.Max(required, checked(_states.Length * 2));
        var replacement = _statePool.Rent(newSize);
        _states.AsSpan(0, _termHash.Count - 1).CopyTo(replacement);
        _statePool.Return(_states, clearArray: false);
        _states = replacement;
        Volatile.Write(ref _accountingDirty, 1);
    }

    private void ThrowIfMutable()
    {
        ThrowIfDisposed();
        Debug.Assert(Volatile.Read(ref _frozen) == 0);
        if (Volatile.Read(ref _frozen) != 0)
            throw new InvalidOperationException("A frozen postings store cannot be mutated.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private void RefreshAllocatedBytes()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            Volatile.Write(ref _allocatedBytes, 0);
            Volatile.Write(ref _accountingDirty, 0);
            return;
        }

        long bytes = _termHash.AllocatedBytes;
        bytes = checked(bytes + _arena.AllocatedBytes);
        bytes = checked(bytes + (long)_states.LongLength * Unsafe.SizeOf<PostingTermState>());
        bytes = checked(bytes + (long)_fieldNames.Capacity * IntPtr.Size);
        bytes = checked(bytes + (long)_fieldPrefixesUtf8.Capacity * IntPtr.Size);
        bytes = checked(bytes + Volatile.Read(ref _fieldPrefixBytes));
        Volatile.Write(ref _allocatedBytes, bytes);
        Volatile.Write(ref _accountingDirty, 0);
    }

    private void RefreshAllocatedBytesIfCapacityChanged(int termHashVersion, int arenaVersion)
    {
        if (termHashVersion != _termHash.AllocationVersion
            || arenaVersion != _arena.AllocationVersion
            || Volatile.Read(ref _accountingDirty) != 0)
        {
            RefreshAllocatedBytes();
        }
    }
}

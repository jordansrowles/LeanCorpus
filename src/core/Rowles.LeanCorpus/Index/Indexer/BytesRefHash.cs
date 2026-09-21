using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Open-addressing hash table for term bytes, modelled on Lucene's BytesRefHash.
/// Stores UTF-8 term data in a flat byte pool with parallel offset/length/hash arrays,
/// avoiding string interning and per-term heap allocations.
/// </summary>
/// <remarks>
/// Hash slots use linear probing with cached hash codes — no re-hashing per probe.
/// The term pool grows by doubling; rehash copies term offsets in bulk.
/// </remarks>
internal sealed class BytesRefHash
{
    private const int DefaultCapacity = 8192;
    private const float LoadFactor = 0.5f;

    private byte[] _pool;
    private readonly ArrayPool<byte> _bytePool;
    private int _poolUsed;
    private int _poolCapacity;

    private int[] _ids;           // hash slot → compact id, or -1
    private int _mask;            // _ids.Length - 1 (always power of 2)

    private int[] _termStarts;    // compact id → byte offset in _pool
    private int[] _termLengths;   // compact id → byte length
    private int[] _termHashes;    // compact id → cached hash

    private int _count;
    private int _capacity;        // _termStarts.Length
    private int _disposed;
    private int _allocationVersion;

    public int Count => _count;

    /// <summary>Gets the bytes reserved by the term pool and hash metadata arrays.</summary>
    public long AllocatedBytes
    {
        get
        {
            if (Volatile.Read(ref _disposed) != 0)
                return 0;
            return _pool.LongLength +
                ((long)_ids.Length + _termStarts.Length + _termLengths.Length + _termHashes.Length) * sizeof(int);
        }
    }

    internal int AllocationVersion => Volatile.Read(ref _allocationVersion);

    public BytesRefHash(int initialCapacity = DefaultCapacity)
        : this(initialCapacity, ArrayPool<byte>.Shared)
    {
    }

    internal BytesRefHash(int initialCapacity, ArrayPool<byte> bytePool)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        ArgumentNullException.ThrowIfNull(bytePool);
        _bytePool = bytePool;
        _capacity = Math.Max(16, initialCapacity);
        _ids = new int[NextPowerOfTwo((int)(_capacity / LoadFactor))];
        _mask = _ids.Length - 1;
        Array.Fill(_ids, -1);

        _poolCapacity = _capacity * 32; // rough estimate: 32 bytes per term average
        _pool = _bytePool.Rent(_poolCapacity);
        _poolUsed = 0;

        _termStarts = new int[_capacity];
        _termLengths = new int[_capacity];
        _termHashes = new int[_capacity];
    }

    /// <summary>
    /// Adds a term (as UTF-8 bytes) and returns its compact ID.
    /// If the term already exists, returns the existing compact ID with a negative sign
    /// (i.e. returns <c>-(id + 1)</c> to distinguish from a new positive ID).
    /// </summary>
    public int Add(ReadOnlySpan<byte> term)
    {
        ThrowIfDisposed();
        int hash = ComputeHash(term);
        int slot = hash & _mask;

        while (true)
        {
            int id = _ids[slot];
            if (id < 0)
            {
                // Empty slot — insert new term
                if (_count >= _capacity) GrowTermArrays();
                EnsurePoolCapacity(_poolUsed + term.Length);

                int newId = _count++;
                term.CopyTo(_pool.AsSpan(_poolUsed));
                _termStarts[newId] = _poolUsed;
                _termLengths[newId] = term.Length;
                _termHashes[newId] = hash;
                _poolUsed += term.Length;
                _ids[slot] = newId;

                if (_count > _ids.Length * LoadFactor)
                    Rehash();

                return newId;
            }

            if (_termHashes[id] == hash && TermEquals(id, term))
                return -(id + 1); // already exists

            slot = (slot + 1) & _mask;
        }
    }

    /// <summary>
    /// Finds a term and returns its compact ID, or a negative value if not found.
    /// </summary>
    public int Find(ReadOnlySpan<byte> term)
    {
        ThrowIfDisposed();
        int hash = ComputeHash(term);
        int slot = hash & _mask;

        while (true)
        {
            int id = _ids[slot];
            if (id < 0)
                return -1;

            if (_termHashes[id] == hash && TermEquals(id, term))
                return id;

            slot = (slot + 1) & _mask;
        }
    }

    /// <summary>
    /// Returns the term bytes at the given compact ID as a span.
    /// </summary>
    public ReadOnlySpan<byte> GetTerm(int id)
    {
        ThrowIfDisposed();
        if ((uint)id >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(id));
        return _pool.AsSpan(_termStarts[id], _termLengths[id]);
    }

    /// <summary>
    /// Decodes the term at the given compact ID to a string.
    /// </summary>
    public string GetTermString(int id)
    {
        return Encoding.UTF8.GetString(GetTerm(id));
    }

    /// <summary>
    /// Returns the cached hash code for a compact ID.
    /// </summary>
    public int GetHashCode(int id)
    {
        ThrowIfDisposed();
        if ((uint)id >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(id));
        return _termHashes[id];
    }

    /// <summary>
    /// Clears all entries, retaining buffers for reuse.
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();
        Array.Fill(_ids, -1);
        _poolUsed = 0;
        _count = 0;
    }

    /// <summary>
    /// Compares terms at two compact IDs lexicographically by UTF-8 bytes.
    /// Returns &lt;0 if a &lt; b, 0 if equal, &gt;0 if a &gt; b.
    /// </summary>
    public int CompareTerms(int a, int b)
    {
        ThrowIfDisposed();
        var spanA = GetTerm(a);
        var spanB = GetTerm(b);
        return spanA.SequenceCompareTo(spanB);
    }

    private bool TermEquals(int id, ReadOnlySpan<byte> term)
    {
        var stored = GetTerm(id);
        return stored.SequenceEqual(term);
    }

    private static int ComputeHash(ReadOnlySpan<byte> data)
    {
        // FNV-1a 32-bit
        uint hash = 2166136261;
        for (int i = 0; i < data.Length; i++)
        {
            hash ^= data[i];
            hash *= 16777619;
        }
        return (int)hash;
    }

    private void Rehash()
    {
        int newSize = _ids.Length * 2;
        var newIds = new int[newSize];
        Array.Fill(newIds, -1);
        int newMask = newSize - 1;

        for (int id = 0; id < _count; id++)
        {
            int hash = _termHashes[id];
            int slot = hash & newMask;
            while (newIds[slot] >= 0)
                slot = (slot + 1) & newMask;
            newIds[slot] = id;
        }

        _ids = newIds;
        _mask = newMask;
        Interlocked.Increment(ref _allocationVersion);
    }

    private void GrowTermArrays()
    {
        int newCapacity = _capacity * 2;
        Array.Resize(ref _termStarts, newCapacity);
        Array.Resize(ref _termLengths, newCapacity);
        Array.Resize(ref _termHashes, newCapacity);
        _capacity = newCapacity;
        Interlocked.Increment(ref _allocationVersion);
    }

    private void EnsurePoolCapacity(int required)
    {
        if (required <= _poolCapacity) return;
        int newCapacity = Math.Max(_poolCapacity * 2, required);
        var newPool = _bytePool.Rent(newCapacity);
        if (_poolUsed > 0)
            Array.Copy(_pool, newPool, _poolUsed);
        _bytePool.Return(_pool, clearArray: false);
        _pool = newPool;
        _poolCapacity = newCapacity;
        Interlocked.Increment(ref _allocationVersion);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_pool.Length > 0)
            _bytePool.Return(_pool, clearArray: false);
        _pool = [];
        _poolCapacity = 0;
        _poolUsed = 0;
        _count = 0;
        _ids = [];
        _termStarts = [];
        _termLengths = [];
        _termHashes = [];
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private static int NextPowerOfTwo(int v)
    {
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return v + 1;
    }
}

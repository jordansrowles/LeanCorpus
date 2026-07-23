using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Thread-safe bounded LRU whose value-type leases prevent active values from being evicted.
/// </summary>
internal sealed class BoundedLruCache<TKey, TValue> : IDisposable, ILifetimeLeaseOwner
    where TKey : notnull
    where TValue : class, IDisposable
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, Entry> _entries;
    private readonly LinkedList<Entry> _lru = [];
    private readonly Lock _lock = new();
    private long _loadCount;
    private bool _disposed;

    internal BoundedLruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Cache capacity must be at least one.");
        _capacity = capacity;
        _entries = new Dictionary<TKey, Entry>(capacity, comparer);
    }

    internal int Count { get { lock (_lock) return _entries.Count; } }

    internal long LoadCount => Volatile.Read(ref _loadCount);

    internal Lease Acquire(TKey key, Func<TValue> valueFactory)
    {
        Entry entry;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(key, out entry!))
            {
                entry.LeaseCount++;
                Touch(entry);
            }
            else
            {
                entry = new Entry(key, valueFactory) { LeaseCount = 1 };
                entry.Node = _lru.AddFirst(entry);
                _entries.Add(key, entry);
            }
        }

        try
        {
            var value = entry.GetValue();
            if (entry.MarkLoaded())
                Interlocked.Increment(ref _loadCount);
            Trim();
            return new Lease(this, entry, value);
        }
        catch
        {
            RemoveFailedEntry(entry);
            throw;
        }
    }

    private void RemoveFailedEntry(Entry entry)
    {
        lock (_lock)
        {
            entry.LeaseCount--;
            if (_entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(entry.Key);
                if (entry.Node is not null)
                    _lru.Remove(entry.Node);
                entry.Node = null;
            }
        }
    }

    private void Release(Entry entry)
    {
        List<TValue>? toDispose;
        lock (_lock)
        {
            if (entry.LeaseCount > 0)
                entry.LeaseCount--;
            toDispose = CollectEvictions();
        }
        DisposeValues(toDispose);
    }

    void ILifetimeLeaseOwner.ReleaseLease(object token) => Release((Entry)token);

    private void Trim()
    {
        List<TValue>? toDispose;
        lock (_lock)
            toDispose = CollectEvictions();
        DisposeValues(toDispose);
    }

    private List<TValue>? CollectEvictions()
    {
        List<TValue>? values = null;
        while (_entries.Count > (_disposed ? 0 : _capacity))
        {
            var node = _lru.Last;
            while (node is not null && node.Value.LeaseCount != 0)
                node = node.Previous;
            if (node is null)
                break;

            var entry = node.Value;
            _lru.Remove(node);
            _entries.Remove(entry.Key);
            entry.Node = null;
            if (entry.TryGetCreated(out var value))
                (values ??= []).Add(value);
        }
        return values;
    }

    private void Touch(Entry entry)
    {
        if (entry.Node is null || ReferenceEquals(entry.Node, _lru.First))
            return;
        _lru.Remove(entry.Node);
        _lru.AddFirst(entry.Node);
    }

    private static void DisposeValues(List<TValue>? values)
    {
        if (values is null)
            return;
        foreach (var value in values)
            value.Dispose();
    }

    public void Dispose()
    {
        List<TValue>? toDispose;
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            toDispose = CollectEvictions();
        }
        DisposeValues(toDispose);
    }

    internal struct Lease : IDisposable
    {
        private BoundedLruCache<TKey, TValue>? _owner;
        private readonly Entry? _entry;

        internal Lease(BoundedLruCache<TKey, TValue> owner, Entry entry, TValue value)
        {
            _owner = owner;
            _entry = entry;
            Value = value;
        }

        internal TValue Value { get; }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null && _entry is not null)
                owner.Release(_entry);
        }

        internal LifetimeLease Detach()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            return owner is null || _entry is null
                ? default
                : new LifetimeLease(owner, _entry);
        }
    }

    internal sealed class Entry
    {
        private int _loaded;
        private readonly Func<TValue> _valueFactory;
        private TValue? _value;
        private Exception? _loadException;
        private bool _loading;

        internal Entry(TKey key, Func<TValue> valueFactory)
        {
            Key = key;
            _valueFactory = valueFactory;
        }

        internal TKey Key { get; }
        internal int LeaseCount { get; set; }
        internal LinkedListNode<Entry>? Node { get; set; }

        internal bool MarkLoaded() => Interlocked.Exchange(ref _loaded, 1) == 0;

        internal TValue GetValue()
        {
            lock (this)
            {
                while (_loading)
                    Monitor.Wait(this);
                if (_value is not null)
                    return _value;
                if (_loadException is not null)
                    throw new InvalidOperationException("The cached value failed to load.", _loadException);
                _loading = true;
            }

            try
            {
                var value = _valueFactory();
                lock (this)
                {
                    _value = value;
                    _loading = false;
                    Monitor.PulseAll(this);
                    return value;
                }
            }
            catch (Exception ex)
            {
                lock (this)
                {
                    _loadException = ex;
                    _loading = false;
                    Monitor.PulseAll(this);
                }
                throw;
            }
        }

        internal bool TryGetCreated([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TValue? value)
        {
            lock (this)
            {
                value = _value;
                return value is not null;
            }
        }
    }
}

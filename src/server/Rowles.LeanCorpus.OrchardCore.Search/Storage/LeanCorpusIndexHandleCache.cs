using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Hosting;
using OrchardCore.Environment.Shell.Scope;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Index;
using SearcherLease = Rowles.LeanCorpus.Index.PhysicalIndexRuntime.PhysicalSearcherLease;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.OrchardCore.Search.Indexing;

namespace Rowles.LeanCorpus.OrchardCore.Search.Storage;

/// <summary>Owns one writer and a refreshed search snapshot for each tenant-local index.</summary>
internal sealed class LeanCorpusIndexHandleCache : IDisposable, IAsyncDisposable
{
    private static readonly object SharedStateLock = new();
    private static readonly StringComparer TenantPathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly Dictionary<SharedStateKey, SharedState> SharedStates = new(new SharedStateKeyComparer());
    private static readonly ConditionalWeakTable<object, RootHostIdentity> RootHostIdentities = new();
    private readonly string _tenantDirectory;
    private readonly RootHostIdentity _rootHostIdentity;
    private readonly SharedStateKey _stateKey;
    private readonly Guid _cacheId = Guid.NewGuid();
    private readonly int? _shellId = ShellScope.Context?.GetHashCode();
    private SharedState? _state;
    private readonly CancellationTokenRegistration _stoppingRegistration;
    private readonly CancellationTokenRegistration _stoppedRegistration;
    private bool _hostStopping;
    private int _disposed;

    public LeanCorpusIndexHandleCache(LeanCorpusIndexPathResolver paths, IHostApplicationLifetime? lifetime = null)
        : this(paths, lifetime, manualRootLifetimeContext: null)
    {
    }

    internal LeanCorpusIndexHandleCache(LeanCorpusIndexPathResolver paths, IHostApplicationLifetime? lifetime, object? manualRootLifetimeContext)
    {
        _tenantDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.TenantDirectory));
        object rootLifetime = manualRootLifetimeContext ?? (object?)lifetime ?? new object();
        lock (SharedStateLock)
            _rootHostIdentity = RootHostIdentities.GetValue(rootLifetime, static _ => new RootHostIdentity());
        _stateKey = new SharedStateKey(_rootHostIdentity, _tenantDirectory);
        if (lifetime is not null)
        {
            _stoppingRegistration = lifetime.ApplicationStopping.Register(OnHostStopping);
            _stoppedRegistration = lifetime.ApplicationStopped.Register(OnHostStopped);
        }
    }

    public async Task<T> WithExclusiveAsync<T>(LeanCorpusIndexPaths paths, Func<Entry, T> operation)
    {
        var entry = await GetEntryAsync(paths).ConfigureAwait(false);
        await entry.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            return operation(entry);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async Task<T> WithExclusiveAsync<T>(LeanCorpusIndexPaths paths, Func<Entry, Task<T>> operation)
    {
        var entry = await GetEntryAsync(paths).ConfigureAwait(false);
        await entry.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            return await operation(entry).ConfigureAwait(false);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async Task<T> WithSearcherAsync<T>(
        LeanCorpusIndexPaths paths,
        Func<IndexSearcher, T> operation,
        LeanCorpusSchemaManifest? manifest = null)
    {
        var entry = await GetEntryAsync(paths).ConfigureAwait(false);
        await entry.Gate.WaitAsync().ConfigureAwait(false);
        SearcherLease lease;
        try
        {
            ThrowIfUnavailable();
            lease = entry.Open(createIfMissing: false, manifest).AcquireSearcher();
        }
        finally
        {
            entry.Gate.Release();
        }

        using (lease)
            return operation(lease.Searcher);
    }

    public async Task<T> WithSearcherAsync<T>(
        LeanCorpusIndexPaths paths,
        Func<IndexSearcher, LeanCorpusSchemaManifest, T> operation,
        Func<Entry, Task<LeanCorpusSchemaManifest>> manifestFactory)
    {
        var entry = await GetEntryAsync(paths).ConfigureAwait(false);
        await entry.Gate.WaitAsync().ConfigureAwait(false);
        SearcherLease lease;
        LeanCorpusSchemaManifest manifest;
        try
        {
            ThrowIfUnavailable();
            manifest = await manifestFactory(entry).ConfigureAwait(false);
            lease = entry.Open(createIfMissing: false, manifest).AcquireSearcher();
        }
        finally
        {
            entry.Gate.Release();
        }

        using (lease)
            return operation(lease.Searcher, manifest);
    }

    private async Task<Entry> GetEntryAsync(LeanCorpusIndexPaths paths)
    {
        while (true)
        {
            Task? previousClose = null;
            lock (SharedStateLock)
            {
                ThrowIfUnavailable();
                if (_state is null)
                {
                    if (!SharedStates.TryGetValue(_stateKey, out var shared))
                    {
                        KeyValuePair<SharedStateKey, SharedState> conflictingRoot = SharedStates.FirstOrDefault(
                            pair => TenantPathComparer.Equals(pair.Key.TenantDirectory, _tenantDirectory));
                        if (conflictingRoot.Key is not null)
                        {
                            if (conflictingRoot.Value.IsClosing)
                                previousClose = conflictingRoot.Value.Closed.Task;
                            else
                                throw new InvalidOperationException("The tenant index path is already owned by another Orchard root host in this process.");
                        }
                        else
                        {
                            shared = new SharedState(_rootHostIdentity, _tenantDirectory);
                            SharedStates.Add(_stateKey, shared);
                        }
                    }
                    if (previousClose is null && shared!.IsClosing)
                        previousClose = shared.Closed.Task;
                    else if (previousClose is null)
                    {
                        _state = shared;
                        int previousOwners = shared.Owners.Count;
                        shared.Owners.Add(_cacheId);
                        TraceOwnership("acquire", shared, previousOwners);
                    }
                }
                if (previousClose is null)
                    return _state!.Entries.GetOrAdd(paths.IndexDirectory, _ => new Entry(paths));
            }
            // Shell construction never synchronously waits for a previous owner.
            await previousClose.ConfigureAwait(false);
        }
    }

    private void ThrowIfUnavailable()
    {
        lock (SharedStateLock)
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0 || _hostStopping || _state?.IsClosing == true, this);
    }

    private void OnHostStopping()
    {
        SharedState? closing = null;
        lock (SharedStateLock)
        {
            _hostStopping = true;
            if (_state is not null && !_state.IsClosing)
            {
                _state.IsClosing = true;
                int previousOwners = _state.Owners.Count;
                _state.Owners.Clear();
                TraceOwnership("host-stop", _state, previousOwners);
                closing = _state;
            }
        }
        if (closing is not null)
            _ = CloseStateAsync(_stateKey, closing);
    }

    private void OnHostStopped()
    {
        Task? closing;
        lock (SharedStateLock) closing = _state?.Closed.Task;
        // ApplicationStopped is a synchronous host signal. Join the asynchronous
        // close started at ApplicationStopping before terminal host stop returns.
        closing?.GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        SharedState? closing = null;
        bool startClose = false;
        lock (SharedStateLock)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && _state is not null)
            {
                int previousOwners = _state.Owners.Count;
                _state.Owners.Remove(_cacheId);
                TraceOwnership("release", _state, previousOwners);
                if (_state.Owners.Count == 0 && !_state.IsClosing)
                {
                    _state.IsClosing = true;
                    startClose = true;
                }
            }
            if (_state?.IsClosing == true) closing = _state;
        }
        try
        {
            if (startClose)
                _ = CloseStateAsync(_stateKey, closing!);
            if (closing is not null)
                await closing.Closed.Task.ConfigureAwait(false);
        }
        finally
        {
            _stoppingRegistration.Dispose();
            _stoppedRegistration.Dispose();
        }
    }

    private static async Task CloseStateAsync(SharedStateKey stateKey, SharedState state)
    {
        using var activity = LeanCorpusActivitySource.Source.StartActivity("leancorpus.orchard.shared.close");
        activity?.SetTag("shared_state.id", state.Id);
        activity?.SetTag("root_host.id", state.RootHostIdentity.Id);
        activity?.SetTag("tenant.id", Path.GetFileName(Path.GetDirectoryName(state.TenantDirectory)));
        var failures = new List<Exception>();
        foreach (var pair in state.Entries)
        {
            Entry entry = pair.Value;
            await entry.Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await entry.CloseAsync().ConfigureAwait(false);
                state.Entries.TryRemove(pair.Key, out _);
            }
            catch (Exception failure) { failures.Add(failure); }
            finally { entry.Gate.Release(); }
        }
        lock (SharedStateLock)
        {
            if (failures.Count == 0)
            {
                if (SharedStates.TryGetValue(stateKey, out var current) && ReferenceEquals(current, state))
                    SharedStates.Remove(stateKey);
                state.Closed.TrySetResult();
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
            }
            else
            {
                // Keep the failed owner discoverable. A new shell must observe
                // this failure instead of opening beside retained resources.
                state.Closed.TrySetException(new AggregateException("Orchard physical index shutdown failed.", failures));
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, failures[0].Message);
            }
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    internal OrchardIndexOwnershipDiagnostics GetDiagnostics()
    {
        lock (SharedStateLock)
            return new(_rootHostIdentity.Id, Path.GetFileName(Path.GetDirectoryName(_tenantDirectory)), _cacheId, _shellId, _state?.Id, _state?.Owners.Count ?? 0,
                _state?.Owners.ToArray() ?? [],
                _hostStopping || Volatile.Read(ref _disposed) != 0 || _state?.IsClosing == true,
                _state?.Closed.Task.IsCompletedSuccessfully ?? false,
                _state?.Entries.Values.Select(entry => entry.Handle).OfType<LeanCorpusIndexHandle>()
                    .Select(handle => handle.RuntimeId).ToArray() ?? [],
                _state?.Closed.Task.Exception?.GetBaseException());
    }

    private void TraceOwnership(string reason, SharedState state, int previousOwners)
    {
        using var activity = LeanCorpusActivitySource.Source.StartActivity("leancorpus.orchard.ownership");
        activity?.SetTag("ownership.reason", reason);
        activity?.SetTag("cache.id", _cacheId);
        activity?.SetTag("root_host.id", _rootHostIdentity.Id);
        activity?.SetTag("tenant.id", Path.GetFileName(Path.GetDirectoryName(_tenantDirectory)));
        activity?.SetTag("shell.id", _shellId);
        activity?.SetTag("shared_state.id", state.Id);
        activity?.SetTag("owners.before", previousOwners);
        activity?.SetTag("owners.after", state.Owners.Count);
    }

    private sealed class SharedState
    {
        internal SharedState(RootHostIdentity rootHostIdentity, string tenantDirectory)
        {
            RootHostIdentity = rootHostIdentity;
            TenantDirectory = tenantDirectory;
        }

        internal ConcurrentDictionary<string, Entry> Entries { get; } = new(StringComparer.Ordinal);
        internal TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Guid Id { get; } = Guid.NewGuid();
        internal RootHostIdentity RootHostIdentity { get; }
        internal string TenantDirectory { get; }
        internal HashSet<Guid> Owners { get; } = [];
        internal bool IsClosing;
    }

    private sealed class RootHostIdentity
    {
        internal Guid Id { get; } = Guid.NewGuid();
    }

    private sealed record SharedStateKey(RootHostIdentity RootIdentity, string TenantDirectory);

    private sealed class SharedStateKeyComparer : IEqualityComparer<SharedStateKey>
    {
        public bool Equals(SharedStateKey? left, SharedStateKey? right) => left is not null && right is not null
            && ReferenceEquals(left.RootIdentity, right.RootIdentity)
            && TenantPathComparer.Equals(left.TenantDirectory, right.TenantDirectory);

        public int GetHashCode(SharedStateKey key) => HashCode.Combine(
            RuntimeHelpers.GetHashCode(key.RootIdentity), TenantPathComparer.GetHashCode(key.TenantDirectory));
    }

    internal sealed class Entry(LeanCorpusIndexPaths paths)
    {
        private LeanCorpusIndexHandle? _handle;

        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal string IndexDirectory => paths.IndexDirectory;
        internal LeanCorpusIndexHandle? Handle => _handle;

        internal LeanCorpusIndexHandle Open(bool createIfMissing, LeanCorpusSchemaManifest? manifest = null)
        {
            if (_handle is not null)
            {
                if (!Directory.Exists(paths.IndexDirectory))
                {
                    Close();
                    if (!createIfMissing)
                        throw new DirectoryNotFoundException($"LeanCorpus index '{paths.IndexFullName}' does not exist.");
                }
                else if (!HasCommit(paths.IndexDirectory))
                {
                    Close();
                    if (!createIfMissing)
                        throw new InvalidDataException($"LeanCorpus index '{paths.IndexFullName}' has no committed index state.");
                }
                else if (manifest is null || _handle.DefaultAnalyserName == manifest.Metadata.DefaultAnalyser)
                {
                    return _handle;
                }
                else
                {
                    Close();
                }
            }

            bool directoryExisted = Directory.Exists(paths.IndexDirectory);
            if (!directoryExisted && !createIfMissing)
                throw new DirectoryNotFoundException($"LeanCorpus index '{paths.IndexFullName}' does not exist.");
            if (directoryExisted && !createIfMissing && !HasCommit(paths.IndexDirectory))
                throw new InvalidDataException($"LeanCorpus index '{paths.IndexFullName}' has no committed index state.");

            PhysicalIndexRuntime? runtime = null;
            try
            {
                string analyserName = manifest?.Metadata.DefaultAnalyser ?? "standard";
                IAnalyser defaultAnalyser = analyserName switch
                {
                    "keyword" => new KeywordAnalyser(),
                    "standard" => new StandardAnalyser(),
                    _ => throw new InvalidDataException("The LeanCorpus index metadata names an unsupported analyser."),
                };
                runtime = new PhysicalIndexRuntime(paths.IndexDirectory, new IndexWriterConfig
                {
                    DurableCommits = true,
                    DefaultAnalyser = defaultAnalyser,
                });
                if (!HasCommit(paths.IndexDirectory))
                {
                    if (!createIfMissing)
                        throw new InvalidDataException($"LeanCorpus index '{paths.IndexFullName}' has no committed index state.");
                    runtime.Commit(refresh: true);
                }

                _handle = new LeanCorpusIndexHandle(runtime, analyserName);
                return _handle;
            }
            catch (Exception openFailure)
            {
                try { runtime?.Dispose(); }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException("Orchard physical index open and cleanup failed.", openFailure, cleanupFailure);
                }
                throw;
            }
        }

        internal void Close()
            => CloseAsync().AsTask().GetAwaiter().GetResult();

        internal async ValueTask CloseAsync()
        {
            if (_handle is not null)
                await _handle.DisposeAsync().ConfigureAwait(false);
            _handle = null;
        }

        private static bool HasCommit(string path)
            => Directory.Exists(path) && Directory.EnumerateFiles(path, "segments_*").Any();
    }
}

/// <summary>Adds Orchard analyser identity and visibility policy to the common physical owner.</summary>
internal sealed class LeanCorpusIndexHandle(PhysicalIndexRuntime runtime, string defaultAnalyserName) : IDisposable, IAsyncDisposable
{
    public IndexWriter Writer => runtime.Writer;
    public string DefaultAnalyserName { get; } = defaultAnalyserName;
    internal Guid RuntimeId => runtime.Id;
    internal PhysicalIndexRuntime.OperationLease EnterOperation() => runtime.EnterOperation();
    public SearcherLease AcquireSearcher() => runtime.AcquireSearcher();
    public void CommitAndRefresh(PhysicalIndexRuntime.OperationLease operation) => runtime.CommitUnderOperation(operation, refresh: true);
    public void Dispose() => runtime.Dispose();
    public ValueTask DisposeAsync() => runtime.DisposeAsync();
}

using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Filesystem-owned store for physical local index handles.</summary>
public sealed class LocalIndexStore : ILocalIndexStore
{
    private readonly string _root;
    private readonly TimeSpan _commitInterval;
    private readonly TimeSpan _refreshInterval;
    private readonly ILocalCommitObserver _observer;
    private readonly ICommitInstallOperations _installOperations;
    private readonly Dictionary<PhysicalIndexId, LocalIndexHandle> _handles = [];
    private readonly Dictionary<PhysicalIndexId, long> _replacementGenerations = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _closing;
    private bool? _lastCreateRollbackSucceeded;
    private Exception? _lastCreateRollbackFailure;

    /// <summary>Creates a physical store rooted at a server-owned directory.</summary>
    public LocalIndexStore(string root, TimeSpan commitInterval, TimeSpan refreshInterval, ILocalCommitObserver? observer = null)
        : this(root, commitInterval, refreshInterval, observer, installOperations: null)
    {
    }

    internal LocalIndexStore(
        string root,
        TimeSpan commitInterval,
        TimeSpan refreshInterval,
        ILocalCommitObserver? observer,
        ICommitInstallOperations? installOperations)
    {
        _root = Path.GetFullPath(root);
        _commitInterval = commitInterval;
        _refreshInterval = refreshInterval;
        _observer = observer ?? NullLocalCommitObserver.Instance;
        _installOperations = installOperations ?? DefaultCommitInstallOperations.Instance;
        Directory.CreateDirectory(_root);
        RecoverAbandonedInstallDirectories();
    }

    /// <inheritdoc />
    public async ValueTask<LocalIndexHandle> CreateAsync(LocalIndexDescriptor descriptor, LocalIndexOpenMode mode = LocalIndexOpenMode.ReadWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfClosing();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfClosing();
            if (_handles.ContainsKey(descriptor.Id) || Exists(descriptor.Id))
                throw new InvalidOperationException("The physical index already exists or is already owned by this store.");
            string path = PathFor(descriptor.Id);
            LocalIndexHandle? handle = null;
            bool pathCreated = false;
            try
            {
                Directory.CreateDirectory(path);
                pathCreated = true;
                handle = new LocalIndexHandle(descriptor, path, descriptor.Settings.CommitInterval ?? _commitInterval, descriptor.Settings.RefreshInterval ?? _refreshInterval, mode, _observer, _installOperations);
                _handles.Add(descriptor.Id, handle);
                return handle;
            }
            catch (Exception createFailure)
            {
                var cleanupFailures = new List<Exception>();
                bool ownerClosed = handle is null;
                if (handle is not null)
                {
                    try { await handle.DisposeAsync().ConfigureAwait(false); ownerClosed = true; }
                    catch (Exception failure) { cleanupFailures.Add(failure); }
                    if (_handles.TryGetValue(descriptor.Id, out LocalIndexHandle? current) && ReferenceEquals(current, handle))
                        _handles.Remove(descriptor.Id);
                }
                bool rollbackCompleted = false;
                if (ownerClosed && pathCreated && !ContainsWriteLockFailure(createFailure) && !HasUnresolvedOpenCleanup(createFailure))
                {
                    try
                    {
                        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                        rollbackCompleted = true;
                    }
                    catch (Exception failure) { cleanupFailures.Add(failure); }
                }
                _lastCreateRollbackSucceeded = rollbackCompleted;
                _lastCreateRollbackFailure = cleanupFailures.FirstOrDefault()
                    ?? (rollbackCompleted ? null : createFailure);
                if (cleanupFailures.Count > 0)
                {
                    var cleanupFailure = new AggregateException("Local index creation and rollback failed; the path was retained for recovery.", [createFailure, .. cleanupFailures]);
                    _lastCreateRollbackFailure = cleanupFailure;
                    throw cleanupFailure;
                }
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<LocalIndexHandle> OpenAsync(LocalIndexDescriptor descriptor, LocalIndexOpenMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfClosing();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfClosing();
            if (_handles.TryGetValue(descriptor.Id, out LocalIndexHandle? existing))
            {
                if (!string.Equals(existing.Descriptor.SchemaHash, descriptor.SchemaHash, StringComparison.Ordinal))
                    throw new InvalidDataException("The physical index is already open with a different schema.");
                if (existing.LifecycleState != LocalIndexHandleLifecycleState.Active)
                {
                    // Disposing is instance-scoped. Join the old owner's shared
                    // completion before considering another physical owner.
                    await existing.DisposeAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (_handles.TryGetValue(descriptor.Id, out LocalIndexHandle? current) && ReferenceEquals(current, existing))
                        _handles.Remove(descriptor.Id);
                    string closedPath = PathFor(descriptor.Id);
                    if (!Directory.Exists(closedPath))
                        throw new DirectoryNotFoundException("The physical index does not exist after its previous owner closed.");
                    LocalIndexHandle replacement = new(descriptor, closedPath, descriptor.Settings.CommitInterval ?? _commitInterval, descriptor.Settings.RefreshInterval ?? _refreshInterval, mode, _observer, _installOperations);
                    _handles.Add(descriptor.Id, replacement);
                    _replacementGenerations[descriptor.Id] = _replacementGenerations.GetValueOrDefault(descriptor.Id) + 1;
                    return replacement;
                }
                if (existing.Mode != mode)
                {
                    if (mode == LocalIndexOpenMode.ReadWrite)
                        await existing.PromoteAsync(cancellationToken).ConfigureAwait(false);
                    else
                        await existing.DemoteAsync(cancellationToken).ConfigureAwait(false);
                }
                return existing;
            }
            string path = PathFor(descriptor.Id);
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("The physical index does not exist.");
            LocalIndexHandle handle = new(descriptor, path, descriptor.Settings.CommitInterval ?? _commitInterval, descriptor.Settings.RefreshInterval ?? _refreshInterval, mode, _observer, _installOperations);
            _handles.Add(descriptor.Id, handle);
            return handle;
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(PhysicalIndexId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfClosing();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfClosing();
            if (_handles.TryGetValue(id, out LocalIndexHandle? handle))
            {
                await handle.DisposeAsync().ConfigureAwait(false);
                _handles.Remove(id);
            }
            string path = PathFor(id);
            if (Directory.Exists(path)) Directory.Delete(path, true);
            _replacementGenerations.Remove(id);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public bool Exists(PhysicalIndexId id) => Directory.Exists(PathFor(id));
    /// <inheritdoc />
    public IReadOnlyList<PhysicalIndexId> List() => Directory.EnumerateDirectories(_root)
        .Select(path => Path.GetFileName(path))
        .Where(static name => name is not null && Guid.TryParseExact(name, "N", out _))
        .Select(static name => new PhysicalIndexId(name!))
        .ToArray();

    internal async ValueTask<LocalIndexStoreDiagnostics> GetLifecycleDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LocalIndexStoreEntryDiagnostics[] entries = _handles.Select(pair =>
            {
                LocalIndexLifecycleDiagnostics lifecycle = pair.Value.GetLifecycleDiagnostics();
                return new LocalIndexStoreEntryDiagnostics(pair.Key.Value, lifecycle.RuntimeId,
                    pair.Value.LifecycleState, _replacementGenerations.GetValueOrDefault(pair.Key));
            }).ToArray();
            return new(Volatile.Read(ref _closing) != 0, entries, _lastCreateRollbackSucceeded, _lastCreateRollbackFailure);
        }
        finally { _gate.Release(); }
    }

    private string PathFor(PhysicalIndexId id)
    {
        if (!Guid.TryParseExact(id.Value, "N", out _))
            throw new ArgumentException("Physical index IDs must be GUID values in N format.", nameof(id));
        return Path.Combine(_root, id.Value);
    }

    private static bool ContainsWriteLockFailure(Exception failure) => failure switch
    {
        WriteLockException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(ContainsWriteLockFailure),
        _ => failure.InnerException is not null && ContainsWriteLockFailure(failure.InnerException)
    };

    private static bool HasUnresolvedOpenCleanup(Exception failure) => failure is AggregateException aggregate
        && (aggregate.InnerExceptions.Count > 1 || aggregate.InnerExceptions.Any(HasUnresolvedOpenCleanup));

    private void RecoverAbandonedInstallDirectories()
    {
        foreach (string directory in Directory.EnumerateDirectories(_root).ToArray())
        {
            string name = Path.GetFileName(directory);
            if (TryGetTemporaryIndexId(name, ".previous-", out PhysicalIndexId id))
            {
                string target = PathFor(id);
                try
                {
                    if (Directory.Exists(target))
                        Directory.Delete(directory, recursive: true);
                    else
                        Directory.Move(directory, target);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Leave an unrecoverable temporary directory for an operator to inspect.
                }
                continue;
            }

            if (name.StartsWith(".install-", StringComparison.Ordinal) || name.StartsWith(".failed-", StringComparison.Ordinal))
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* leave an active or locked staging directory for inspection */ }
            }
        }
    }

    private static bool TryGetTemporaryIndexId(string name, string prefix, out PhysicalIndexId id)
    {
        id = default;
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        ReadOnlySpan<char> remainder = name.AsSpan(prefix.Length);
        int separator = remainder.IndexOf('-');
        if (separator <= 0)
            return false;
        string value = remainder[..separator].ToString();
        if (!Guid.TryParseExact(value, "N", out _))
            return false;
        id = new PhysicalIndexId(value);
        return true;
    }
    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closing, 1) == 0)
            _ = CloseCoreAsync();
        return new ValueTask(_closed.Task);
    }

    private async Task CloseCoreAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var closing = _handles.Select(pair => (pair.Key, Close: pair.Value.DisposeAsync().AsTask())).ToArray();
            try { await Task.WhenAll(closing.Select(entry => entry.Close)).ConfigureAwait(false); }
            catch { /* Collect every failed owner after all close attempts finish. */ }
            var failures = new List<Exception>();
            foreach (var entry in closing)
            {
                if (entry.Close.IsCompletedSuccessfully) _handles.Remove(entry.Key);
                else if (entry.Close.Exception is { } failure) failures.Add(failure);
            }
            if (failures.Count == 0) _closed.TrySetResult();
            else _closed.TrySetException(new AggregateException("Physical index store shutdown failed.", failures));
        }
        catch (Exception failure) { _closed.TrySetException(failure); }
        finally { _gate.Release(); }
    }

    private void ThrowIfClosing() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _closing) != 0, this);
}

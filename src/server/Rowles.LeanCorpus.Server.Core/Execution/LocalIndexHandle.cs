using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Core.Runtime;

namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Supported local runtime boundary that never exposes engine lifecycle objects.</summary>
public sealed class LocalIndexHandle : IAsyncDisposable, IDisposable
{
    private readonly string _path;
    private readonly TimeSpan _commitInterval;
    private readonly TimeSpan _refreshInterval;
    private readonly ILocalCommitObserver _observer;
    private readonly ICommitInstallOperations _installOperations;
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IndexRuntime _runtime;
    private int _disposed;
    private int _installing;
    private int _activeSnapshots;
    private int _unusable;
    private string? _lastInstallError;

    internal LocalIndexHandle(
        LocalIndexDescriptor descriptor,
        string path,
        TimeSpan commitInterval,
        TimeSpan refreshInterval,
        LocalIndexOpenMode mode,
        ILocalCommitObserver? observer = null,
        ICommitInstallOperations? installOperations = null)
    {
        Descriptor = descriptor;
        _path = path;
        _commitInterval = commitInterval;
        _refreshInterval = refreshInterval;
        observer ??= NullLocalCommitObserver.Instance;
        _observer = observer;
        _installOperations = installOperations ?? DefaultCommitInstallOperations.Instance;
        _runtime = new IndexRuntime(path, CompiledIndexSchema.Create(descriptor.Schema, descriptor.Topology ?? new(1, 0), descriptor.Settings), commitInterval, refreshInterval, mode,
            receipt => _observer.OnCommittedAsync(descriptor, receipt));
    }

    /// <summary>Gets the server-level descriptor.</summary>
    public LocalIndexDescriptor Descriptor { get; }

    /// <summary>Gets the runtime for the internal Community composition boundary.</summary>
    internal IndexRuntime Runtime => _runtime;

    internal LocalIndexLifecycleDiagnostics GetLifecycleDiagnostics()
    {
        IndexRuntime runtime = _runtime;
        var physical = runtime.GetPhysicalDiagnostics();
        return new(Descriptor.Id.Value, physical.RuntimeId, runtime.Mode, runtime.CurrentServerSequence,
            Volatile.Read(ref _activeSnapshots), runtime.Commits.GetDiagnostics(), physical);
    }

    internal LocalIndexHandleLifecycleState LifecycleState
    {
        get
        {
            if (_closed.Task.IsCompletedSuccessfully) return LocalIndexHandleLifecycleState.Closed;
            if (_closed.Task.IsFaulted || _closed.Task.IsCanceled) return LocalIndexHandleLifecycleState.Failed;
            return Volatile.Read(ref _disposed) == 0
                ? LocalIndexHandleLifecycleState.Active
                : LocalIndexHandleLifecycleState.Closing;
        }
    }

    /// <summary>Gets the current access mode.</summary>
    public LocalIndexOpenMode Mode => _runtime.Mode;

    /// <summary>Gets the explicit coordinator for this physical index.</summary>
    public ILocalCommitCoordinator CommitCoordinator => _runtime.Commits;

    /// <summary>Gets local health and commit state.</summary>
    public LocalIndexHealth Health
    {
        get
        {
            long visibleGeneration = 0;
            try
            {
                using SearcherLease visible = _runtime.Searchers.AcquireLease();
                visibleGeneration = visible.CommitGeneration;
            }
            catch (ObjectDisposedException)
            {
                // The unusable state below is the public indication that no
                // readable runtime remains available.
            }

            long durableGeneration = Math.Max(_runtime.Commits.LastReceipt?.CommitGeneration ?? 0, visibleGeneration);
            return new LocalIndexHealth(
                Mode,
                visibleGeneration,
                durableGeneration,
                _runtime.PendingOperations,
                _runtime.Commits.LastSuccessfulCommitUtc,
                _runtime.Commits.LastFailure?.Message,
                _runtime.Commits.ConsecutiveFailures,
                Volatile.Read(ref _activeSnapshots),
                Volatile.Read(ref _installing) != 0,
                IsUsable,
                _runtime.IsDegraded || !IsUsable || Volatile.Read(ref _lastInstallError) is not null,
                Volatile.Read(ref _lastInstallError));
        }
    }

    /// <summary>Gets whether this handle still has a readable local runtime.</summary>
    internal bool IsUsable => Volatile.Read(ref _unusable) == 0 && Volatile.Read(ref _disposed) == 0 && _runtime.SearchesAllowed;

    /// <summary>Commits pending writes when this handle is writable.</summary>
    public async ValueTask<CommitResult> CommitAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        ThrowIfClosing();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfClosing();
            return _runtime.Commits.Commit(refresh);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>Waits until the requested local sequence is durable.</summary>
    public ValueTask<LocalCommitReceipt> WaitUntilCommittedAsync(long sequenceNumber, CancellationToken cancellationToken = default) =>
        _runtime.Commits.WaitUntilCommittedAsync(sequenceNumber, cancellationToken);

    /// <summary>Refreshes the local readable generation.</summary>
    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClosing();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfClosing();
            _runtime.Refresh();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>Acquires a lease that pins one committed generation and its files.</summary>
    public async ValueTask<CommitSnapshotLease> AcquireCommitSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        if (Mode != LocalIndexOpenMode.ReadWrite)
            throw new InvalidOperationException("A read-only local copy cannot create a writer snapshot.");
        IndexRuntime runtime = _runtime;
        IDisposable pin = runtime.EnterPhysicalOperation();
        IndexSnapshot? snapshot = null;
        try
        {
            snapshot = runtime.Writer.CreateSnapshot();
            IndexBackupManifest manifest = runtime.Writer.CreateBackupManifest(snapshot);
            var lease = new RuntimeCommitSnapshotLease(runtime, snapshot, manifest, Descriptor.SchemaHash, pin, this);
            Interlocked.Increment(ref _activeSnapshots);
            return lease;
        }
        catch
        {
            try { if (snapshot is not null) runtime.Writer.ReleaseSnapshot(snapshot); }
            finally { pin.Dispose(); }
            throw;
        }
    }

    /// <summary>Atomically installs a pinned commit into this read-only local copy.</summary>
    public async ValueTask<CommitInstallResult> InstallCommitAsync(CommitSnapshotLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ThrowIfClosing();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _operationGate.Release();
            throw;
        }
        try
        {
            ThrowIfClosing();
            if (!IsUsable)
                return new CommitRejected("The local index is unusable after a previous installation failure.");
            if (Mode != LocalIndexOpenMode.ReadOnly)
                return new CommitRejected("A commit cannot be installed into an active writable local copy.");
            if (lease is not RuntimeCommitSnapshotLease source)
                return new CommitRejected("The snapshot lease was not created by this Community local store.");
            if (source is not ICommitSnapshotSource)
                return new CommitRejected("The commit snapshot cannot be materialised by this local store.");
            if (!string.Equals(source.SchemaHash, Descriptor.SchemaHash, StringComparison.Ordinal))
                return new CommitRejected("The commit snapshot schema does not match this local physical index.");
            using (var current = _runtime.Searchers.AcquireLease())
            {
                if (lease.CommitGeneration < current.CommitGeneration)
                    return new CommitRejected($"The commit generation {lease.CommitGeneration} is older than the visible target generation {current.CommitGeneration}.");
                if (current.CommitGeneration == lease.CommitGeneration)
                    return current.ContentToken == lease.ContentToken
                        ? new CommitAlreadyPresent(lease.CommitGeneration)
                        : new CommitRejected("The target already contains a different commit with the same generation.");
            }

            string installRoot = Path.GetDirectoryName(_path)!;
            string staging = Path.Combine(installRoot, $".install-{Descriptor.Id.Value}-{Guid.NewGuid():N}-backup");
            string materialised = Path.Combine(installRoot, $".install-{Descriptor.Id.Value}-{Guid.NewGuid():N}-candidate");
            string previous = Path.Combine(Path.GetDirectoryName(_path)!, $".previous-{Descriptor.Id.Value}-{Guid.NewGuid():N}");
            IndexRuntime oldRuntime = _runtime;
            IndexRuntime? candidateRuntime = null;
            bool publicationStarted = false;
            bool oldRootMoved = false;
            bool preserveInstallArtifacts = false;
            Interlocked.Exchange(ref _installing, 1);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _installOperations.Materialise(lease, staging, materialised, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                // No cancellation checks occur after this point. The directory
                // publication sequence must either finish or execute rollback.
                publicationStarted = true;
                _runtime = oldRuntime;
                await oldRuntime.DisposeAsync().ConfigureAwait(false);
                _installOperations.MoveDirectory(_path, previous);
                oldRootMoved = true;
                _installOperations.MoveDirectory(materialised, _path);
                candidateRuntime = CreateRuntime(LocalIndexOpenMode.ReadOnly);
                _runtime = candidateRuntime;
                candidateRuntime = null;
                Volatile.Write(ref _lastInstallError, null);
                TryDeleteDirectory(previous);
                return new CommitInstalled(new LocalCommitReceipt(0, 0, lease.CommitGeneration, lease.ContentToken, true, true));
            }
            catch (OperationCanceledException) when (!publicationStarted)
            {
                throw;
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _lastInstallError, exception.Message);
                if (!publicationStarted)
                {
                    _runtime.MarkDegraded();
                    return new CommitRejected(exception.Message);
                }

                if (!TryRollbackInstall(oldRuntime, previous, oldRootMoved, candidateRuntime, out Exception? rollbackFailure))
                {
                    preserveInstallArtifacts = true;
                    MarkUnusable(exception, rollbackFailure!);
                    return new CommitRejected($"Commit installation failed and the local index is unusable: {exception.Message}");
                }

                return new CommitRejected(exception.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _installing, 0);
                TryDeleteDirectory(staging);
                if (!preserveInstallArtifacts)
                {
                    TryDeleteDirectory(materialised);
                    TryDeleteDirectory(previous);
                }
            }
        }
        finally
        {
            _transition.Release();
            _operationGate.Release();
        }
    }

    private bool TryRollbackInstall(
        IndexRuntime oldRuntime,
        string previous,
        bool oldRootMoved,
        IndexRuntime? candidateRuntime,
        out Exception? rollbackFailure)
    {
        try
        {
            candidateRuntime?.Dispose();

            if (oldRootMoved)
            {
                if (Directory.Exists(_path) && Directory.Exists(previous))
                {
                    string failed = Path.Combine(Path.GetDirectoryName(_path)!, $".failed-{Descriptor.Id.Value}-{Guid.NewGuid():N}");
                    _installOperations.MoveDirectory(_path, failed);
                    _installOperations.MoveDirectory(previous, _path);
                    TryDeleteDirectory(failed);
                }
                else if (!Directory.Exists(_path) && Directory.Exists(previous))
                {
                    _installOperations.MoveDirectory(previous, _path);
                }
                else if (!Directory.Exists(_path))
                {
                    throw new IOException("The previous local index root is missing and could not be restored.");
                }
            }

            // Reopening the old root is part of rollback. A directory that is
            // present but cannot be reopened is not a healthy readable copy.
            _runtime = CreateRuntime(LocalIndexOpenMode.ReadOnly);
            rollbackFailure = null;
            return true;
        }
        catch (Exception exception)
        {
            rollbackFailure = exception;
            try { oldRuntime.MarkDegraded(); } catch { /* the runtime may already be disposed */ }
            return false;
        }
    }

    private void MarkUnusable(Exception installFailure, Exception rollbackFailure)
    {
        Volatile.Write(ref _lastInstallError, $"{installFailure.Message} Rollback failed: {rollbackFailure.Message}");
        Interlocked.Exchange(ref _unusable, 1);
        _runtime.MarkDegraded();
    }

    /// <summary>Reopens the committed local copy with a writer.</summary>
    public ValueTask PromoteAsync(CancellationToken cancellationToken = default) => TransitionAsync(LocalIndexOpenMode.ReadWrite, cancellationToken);

    /// <summary>Commits and reopens the local copy read-only.</summary>
    public ValueTask DemoteAsync(CancellationToken cancellationToken = default) => TransitionAsync(LocalIndexOpenMode.ReadOnly, cancellationToken);

    private async ValueTask TransitionAsync(LocalIndexOpenMode mode, CancellationToken cancellationToken)
    {
        ThrowIfClosing();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _operationGate.Release();
            throw;
        }
        try
        {
            ThrowIfClosing();
            if (Mode == mode) return;
            if (Mode == LocalIndexOpenMode.ReadWrite)
                _ = _runtime.Commits.Commit(refresh: true);
            await _runtime.DisposeAsync().ConfigureAwait(false);
            _runtime = CreateRuntime(mode);
        }
        finally
        {
            _transition.Release();
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    internal async ValueTask<IDisposable> EnterOperationAsync(CancellationToken cancellationToken)
        => await EnterOperationWithLeaseAsync(cancellationToken).ConfigureAwait(false);

    internal async ValueTask<OperationLease> EnterOperationWithLeaseAsync(CancellationToken cancellationToken)
    {
        ThrowIfClosing();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfClosing();
            if (!IsUsable)
                throw new LocalExecutionException(new ApiFailure("index_unusable", "The local index is unusable after a committed-state installation failure."));
            return new OperationLease(_operationGate, _runtime.EnterPhysicalOperation());
        }
        catch
        {
            _operationGate.Release();
            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _ = CloseCoreAsync();
        return new ValueTask(_closed.Task);
    }

    private async Task CloseCoreAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _runtime.DisposeAsync().ConfigureAwait(false);
            _closed.TrySetResult();
        }
        catch (Exception failure) { _closed.TrySetException(failure); }
        finally
        {
            // Already queued callers must wake, observe closed admission and
            // unwind before these managed gates become collectable.
            _operationGate.Release();
        }
    }

    private void ThrowIfClosing() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private IndexRuntime CreateRuntime(LocalIndexOpenMode mode) =>
        new(_path, CompiledIndexSchema.Create(Descriptor.Schema, Descriptor.Topology ?? new(1, 0), Descriptor.Settings), _commitInterval, _refreshInterval, mode,
            receipt => _observer.OnCommittedAsync(Descriptor, receipt));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An abandoned temporary directory is safe to inspect and recover at the next startup.
        }
    }

    internal sealed class OperationLease : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private readonly PhysicalIndexRuntime.OperationLease _physicalOperation;
        private int _released;
        internal PhysicalIndexRuntime.OperationLease PhysicalOperation => _physicalOperation;

        internal OperationLease(SemaphoreSlim gate, PhysicalIndexRuntime.OperationLease physicalOperation)
        {
            _gate = gate;
            _physicalOperation = physicalOperation;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                try { _physicalOperation.Dispose(); }
                finally { _gate.Release(); }
            }
        }
    }

    private sealed class RuntimeCommitSnapshotLease(IndexRuntime runtime, IndexSnapshot snapshot, IndexBackupManifest manifest, string schemaHash, IDisposable physicalPin, LocalIndexHandle owner) : CommitSnapshotLease, ICommitSnapshotSource
    {
        private readonly object _lifetimeGate = new();
        private bool _disposeRequested;
        private int _activeUses;
        private int _released;
        internal string SchemaHash => schemaHash;
        public override long CommitGeneration => manifest.CommitGeneration;
        public override long ContentToken => manifest.ContentToken;
        public override IndexBackupManifest Manifest => manifest;

        public override Stream OpenRead(string fileName)
        {
            AcquireUse();
            try
            {
                if (manifest.Files.All(file => !string.Equals(file.FileName, fileName, StringComparison.Ordinal)))
                    throw new ArgumentException("The file is not part of this commit snapshot.", nameof(fileName));
                return new SnapshotReadStream(Path.Combine(runtime.Path, fileName), this);
            }
            catch { ReleaseUse(); throw; }
        }

        public void CreateBackup(string path, CancellationToken cancellationToken)
        {
            AcquireUse();
            try { runtime.Writer.BackupSnapshot(snapshot, path, new IndexBackupOptions { OverwriteBackupDirectory = true }, cancellationToken); }
            finally { ReleaseUse(); }
        }

        private void AcquireUse()
        {
            lock (_lifetimeGate)
            {
                ObjectDisposedException.ThrowIf(_disposeRequested, this);
                _activeUses++;
            }
        }

        private void ReleaseUse()
        {
            bool release;
            lock (_lifetimeGate) release = --_activeUses == 0 && _disposeRequested;
            if (release) ReleaseSnapshot();
        }

        public override void Dispose()
        {
            bool release;
            lock (_lifetimeGate)
            {
                if (_disposeRequested) return;
                _disposeRequested = true;
                release = _activeUses == 0;
            }
            if (release) ReleaseSnapshot();
        }

        private void ReleaseSnapshot()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            try { runtime.Writer.ReleaseSnapshot(snapshot); }
            finally
            {
                Interlocked.Decrement(ref owner._activeSnapshots);
                physicalPin.Dispose();
            }
        }

        private sealed class SnapshotReadStream(string path, RuntimeCommitSnapshotLease owner) : FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
        {
            private int _released;
            protected override void Dispose(bool disposing)
            {
                try { base.Dispose(disposing); }
                finally { if (disposing) Release(); }
            }
            public override async ValueTask DisposeAsync()
            {
                try { await base.DisposeAsync().ConfigureAwait(false); }
                finally { Release(); }
            }
            private void Release()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0) owner.ReleaseUse();
            }
        }
    }
}

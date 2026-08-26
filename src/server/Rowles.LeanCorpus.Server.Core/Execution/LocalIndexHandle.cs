using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Server.Core.Runtime;

namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Supported local runtime boundary that never exposes engine lifecycle objects.</summary>
public sealed class LocalIndexHandle : IAsyncDisposable, IDisposable
{
    private readonly string _path;
    private readonly TimeSpan _commitInterval;
    private readonly TimeSpan _refreshInterval;
    private readonly ILocalCommitObserver _observer;
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private IndexRuntime _runtime;
    private int _disposed;

    internal LocalIndexHandle(LocalIndexDescriptor descriptor, string path, TimeSpan commitInterval, TimeSpan refreshInterval, LocalIndexOpenMode mode, ILocalCommitObserver? observer = null)
    {
        Descriptor = descriptor;
        _path = path;
        _commitInterval = commitInterval;
        _refreshInterval = refreshInterval;
        observer ??= NullLocalCommitObserver.Instance;
        _observer = observer;
        _runtime = new IndexRuntime(path, CompiledIndexSchema.Create(descriptor.Schema, descriptor.Topology ?? new(1, 0), descriptor.Settings), commitInterval, refreshInterval, mode,
            receipt => _observer.OnCommittedAsync(descriptor, receipt));
    }

    /// <summary>Gets the server-level descriptor.</summary>
    public LocalIndexDescriptor Descriptor { get; }

    /// <summary>Gets the runtime for the internal Community composition boundary.</summary>
    internal IndexRuntime Runtime => _runtime;

    /// <summary>Gets the current access mode.</summary>
    public LocalIndexOpenMode Mode => _runtime.Mode;

    /// <summary>Gets the explicit coordinator for this physical index.</summary>
    public ILocalCommitCoordinator CommitCoordinator => _runtime.Commits;

    /// <summary>Gets local health and commit state.</summary>
    public LocalIndexHealth Health => new(
        Mode,
        _runtime.Commits.LastReceipt?.CommitGeneration ?? 0,
        _runtime.Commits.LastReceipt?.CommitGeneration ?? 0,
        _runtime.PendingOperations,
        null,
        _runtime.Commits.LastFailure?.Message,
        _runtime.Commits.ConsecutiveFailures,
        0,
        false);

    /// <summary>Commits pending writes when this handle is writable.</summary>
    public async ValueTask<CommitResult> CommitAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _runtime.Refresh();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>Acquires a lease that pins one committed generation and its files.</summary>
    public ValueTask<CommitSnapshotLease> AcquireCommitSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Mode != LocalIndexOpenMode.ReadWrite)
            throw new InvalidOperationException("A read-only local copy cannot create a writer snapshot.");
        IndexSnapshot snapshot = _runtime.Writer.CreateSnapshot();
        IndexBackupManifest manifest = _runtime.Writer.CreateBackupManifest(snapshot);
        return ValueTask.FromResult<CommitSnapshotLease>(new RuntimeCommitSnapshotLease(_runtime, snapshot, manifest, Descriptor.SchemaHash));
    }

    /// <summary>Atomically installs a pinned commit into this read-only local copy.</summary>
    public async ValueTask<CommitInstallResult> InstallCommitAsync(CommitSnapshotLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
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
            if (Mode != LocalIndexOpenMode.ReadOnly)
                return new CommitRejected("A commit cannot be installed into an active writable local copy.");
            if (lease is not RuntimeCommitSnapshotLease source)
                return new CommitRejected("The snapshot lease was not created by this Community local store.");
            if (!string.Equals(source.SchemaHash, Descriptor.SchemaHash, StringComparison.Ordinal))
                return new CommitRejected("The commit snapshot schema does not match this local physical index.");
            using (var current = _runtime.Searchers.AcquireLease())
            {
                if (current.CommitGeneration == lease.CommitGeneration)
                    return current.ContentToken == lease.ContentToken
                        ? new CommitAlreadyPresent(lease.CommitGeneration)
                        : new CommitRejected("The target already contains a different commit with the same generation.");
            }

            string staging = Path.Combine(Path.GetTempPath(), $"leancorpus-server-install-{Guid.NewGuid():N}");
            string materialised = Path.Combine(Path.GetDirectoryName(_path)!, $".install-{Descriptor.Id.Value}-{Guid.NewGuid():N}");
            string previous = Path.Combine(Path.GetDirectoryName(_path)!, $".previous-{Descriptor.Id.Value}-{Guid.NewGuid():N}");
            bool runtimeDisposed = false;
            try
            {
                source.CreateBackup(staging);
                IndexBackup.Restore(staging, materialised, new IndexRestoreOptions { OverwriteTargetDirectory = false, ValidateAfterRestore = true });
                _runtime.Dispose();
                runtimeDisposed = true;
                Directory.Move(_path, previous);
                Directory.Move(materialised, _path);
                _runtime = CreateRuntime(LocalIndexOpenMode.ReadOnly);
                TryDeleteDirectory(previous);
                return new CommitInstalled(new LocalCommitReceipt(0, 0, lease.CommitGeneration, lease.ContentToken, true, true));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
            {
                try
                {
                    if (Directory.Exists(_path) && Directory.Exists(previous))
                    {
                        string failed = Path.Combine(Path.GetDirectoryName(_path)!, $".failed-{Descriptor.Id.Value}-{Guid.NewGuid():N}");
                        Directory.Move(_path, failed);
                        Directory.Move(previous, _path);
                        TryDeleteDirectory(failed);
                    }
                    else if (!Directory.Exists(_path) && Directory.Exists(previous))
                    {
                        Directory.Move(previous, _path);
                    }
                    if (runtimeDisposed && Directory.Exists(_path))
                        _runtime = CreateRuntime(LocalIndexOpenMode.ReadOnly);
                }
                catch
                {
                    // Preserve the original install failure. The handle remains unusable if the filesystem cannot restore its prior root.
                }
                return new CommitRejected(exception.Message);
            }
            finally
            {
                TryDeleteDirectory(staging);
                TryDeleteDirectory(materialised);
                TryDeleteDirectory(previous);
            }
        }
        finally
        {
            _transition.Release();
            _operationGate.Release();
        }
    }

    /// <summary>Reopens the committed local copy with a writer.</summary>
    public ValueTask PromoteAsync(CancellationToken cancellationToken = default) => TransitionAsync(LocalIndexOpenMode.ReadWrite, cancellationToken);

    /// <summary>Commits and reopens the local copy read-only.</summary>
    public ValueTask DemoteAsync(CancellationToken cancellationToken = default) => TransitionAsync(LocalIndexOpenMode.ReadOnly, cancellationToken);

    private async ValueTask TransitionAsync(LocalIndexOpenMode mode, CancellationToken cancellationToken)
    {
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
            if (Mode == mode) return;
            if (Mode == LocalIndexOpenMode.ReadWrite)
                _ = _runtime.Commits.Commit(refresh: true);
            _runtime.Dispose();
            _runtime = CreateRuntime(mode);
        }
        finally
        {
            _transition.Release();
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _operationGate.Wait();
        try
        {
            _runtime.Dispose();
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
            _transition.Dispose();
        }
    }

    internal async ValueTask<IDisposable> EnterOperationAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new OperationLease(_operationGate);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

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

    private sealed class OperationLease(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate.Release();
        }
    }

    private sealed class RuntimeCommitSnapshotLease(IndexRuntime runtime, IndexSnapshot snapshot, IndexBackupManifest manifest, string schemaHash) : CommitSnapshotLease
    {
        private int _disposed;
        internal string SchemaHash => schemaHash;
        public override long CommitGeneration => manifest.CommitGeneration;
        public override long ContentToken => manifest.ContentToken;
        public override IndexBackupManifest Manifest => manifest;
        public override Stream OpenRead(string fileName)
        {
            if (manifest.Files.All(file => !string.Equals(file.FileName, fileName, StringComparison.Ordinal)))
                throw new ArgumentException("The file is not part of this commit snapshot.", nameof(fileName));
            return File.OpenRead(Path.Combine(runtime.Path, fileName));
        }
        internal void CreateBackup(string path) => runtime.Writer.BackupSnapshot(snapshot, path, new IndexBackupOptions { OverwriteBackupDirectory = true });
        public override void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                runtime.Writer.ReleaseSnapshot(snapshot);
        }
    }
}

using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Filesystem-owned store for physical local index handles.</summary>
public sealed class LocalIndexStore : ILocalIndexStore
{
    private readonly string _root;
    private readonly TimeSpan _commitInterval;
    private readonly TimeSpan _refreshInterval;
    private readonly ILocalCommitObserver _observer;
    private readonly Dictionary<PhysicalIndexId, LocalIndexHandle> _handles = [];

    /// <summary>Creates a physical store rooted at a server-owned directory.</summary>
    public LocalIndexStore(string root, TimeSpan commitInterval, TimeSpan refreshInterval, ILocalCommitObserver? observer = null)
    {
        _root = Path.GetFullPath(root);
        _commitInterval = commitInterval;
        _refreshInterval = refreshInterval;
        _observer = observer ?? NullLocalCommitObserver.Instance;
        Directory.CreateDirectory(_root);
        RecoverAbandonedInstallDirectories();
    }

    /// <inheritdoc />
    public ValueTask<LocalIndexHandle> CreateAsync(LocalIndexDescriptor descriptor, LocalIndexOpenMode mode = LocalIndexOpenMode.ReadWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        if (Exists(descriptor.Id)) throw new InvalidOperationException("The physical index already exists.");
        string path = PathFor(descriptor.Id);
        Directory.CreateDirectory(path);
        LocalIndexHandle handle = new(descriptor, path, descriptor.Settings.CommitInterval ?? _commitInterval, descriptor.Settings.RefreshInterval ?? _refreshInterval, mode, _observer);
        _handles.Add(descriptor.Id, handle);
        return ValueTask.FromResult(handle);
    }

    /// <inheritdoc />
    public async ValueTask<LocalIndexHandle> OpenAsync(LocalIndexDescriptor descriptor, LocalIndexOpenMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        if (_handles.TryGetValue(descriptor.Id, out LocalIndexHandle? existing))
        {
            if (!string.Equals(existing.Descriptor.SchemaHash, descriptor.SchemaHash, StringComparison.Ordinal))
                throw new InvalidDataException("The physical index is already open with a different schema.");
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
        LocalIndexHandle handle = new(descriptor, path, descriptor.Settings.CommitInterval ?? _commitInterval, descriptor.Settings.RefreshInterval ?? _refreshInterval, mode, _observer);
        _handles.Add(descriptor.Id, handle);
        return handle;
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(PhysicalIndexId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_handles.Remove(id, out LocalIndexHandle? handle)) handle.Dispose();
        string path = PathFor(id);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public bool Exists(PhysicalIndexId id) => Directory.Exists(PathFor(id));
    /// <inheritdoc />
    public IReadOnlyList<PhysicalIndexId> List() => Directory.EnumerateDirectories(_root)
        .Select(path => Path.GetFileName(path))
        .Where(static name => name is not null && Guid.TryParseExact(name, "N", out _))
        .Select(static name => new PhysicalIndexId(name!))
        .ToArray();

    private string PathFor(PhysicalIndexId id)
    {
        if (!Guid.TryParseExact(id.Value, "N", out _))
            throw new ArgumentException("Physical index IDs must be GUID values in N format.", nameof(id));
        return Path.Combine(_root, id.Value);
    }

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
    public ValueTask DisposeAsync() { foreach (LocalIndexHandle handle in _handles.Values) handle.Dispose(); _handles.Clear(); return ValueTask.CompletedTask; }
}

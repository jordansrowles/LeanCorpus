using Rowles.LeanCorpus.Server.Core.Storage;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Execution;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Owns registered local indices and prevents implicit creation.</summary>
internal sealed class LocalIndexRegistry : IDisposable
{
    private readonly string _indicesPath;
    private readonly RegistryStore _store;
    private readonly LocalIndexStore _physicalStore;
    private readonly TimeSpan _commitInterval;
    private readonly TimeSpan _refreshInterval;
    private readonly Dictionary<string, IndexRuntimeEntry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private LocalIndexRegistry(string dataRoot, TimeSpan commitInterval, TimeSpan refreshInterval, ILocalCommitObserver? observer)
    {
        _indicesPath = Path.Combine(dataRoot, "indices");
        _store = new RegistryStore(dataRoot);
        _physicalStore = new LocalIndexStore(_indicesPath, commitInterval, refreshInterval, observer);
        _commitInterval = commitInterval;
        _refreshInterval = refreshInterval;
    }

    internal static async ValueTask<LocalIndexRegistry> OpenAsync(string dataRoot, ServerCoreOptions options, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(dataRoot);
        LocalIndexRegistry registry = new(dataRoot, options.CommitInterval, options.RefreshInterval, options.CommitObserver);
        Directory.CreateDirectory(registry._indicesPath);

        ServerRegistry persisted = await registry._store.LoadAsync(cancellationToken).ConfigureAwait(false);
        ValidatePersistedRegistrations(persisted.Indices, registry._indicesPath);
        foreach (IndexRegistration registration in persisted.Indices)
        {
            LocalIndexDescriptor descriptor = ToDescriptor(registration);
            LocalIndexHandle handle = await registry._physicalStore.OpenAsync(descriptor, LocalIndexOpenMode.ReadWrite, cancellationToken).ConfigureAwait(false);
            registry._entries.Add(registration.Name, new IndexRuntimeEntry(registration, handle));
        }

        return registry;
    }

    internal IReadOnlyList<IndexRuntimeEntry> List()
    {
        lock (_entries)
            return _entries.Values.ToArray();
    }

    internal bool TryGet(string name, out IndexRuntimeEntry? entry)
    {
        lock (_entries)
            return _entries.TryGetValue(name, out entry);
    }

    internal async ValueTask<IndexRuntimeEntry?> CreateAsync(IndexRegistration registration, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_entries)
            {
                if (_entries.ContainsKey(registration.Name))
                    return null;
            }

            LocalIndexHandle handle = await _physicalStore.CreateAsync(ToDescriptor(registration), LocalIndexOpenMode.ReadWrite, cancellationToken).ConfigureAwait(false);
            IndexRuntimeEntry entry = new(registration, handle);
            lock (_entries)
                _entries.Add(registration.Name, entry);

            try
            {
                await PersistAsync(cancellationToken).ConfigureAwait(false);
                return entry;
            }
            catch
            {
                lock (_entries)
                    _entries.Remove(registration.Name);
                await _physicalStore.DeleteAsync(new PhysicalIndexId(registration.Id), CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IndexRuntimeEntry? entry;
            lock (_entries)
            {
                if (!_entries.TryGetValue(name, out entry))
                    return false;

                _entries.Remove(name);
            }

            // Publish the registry change before disposing the runtime. If persistence
            // fails, the live entry and its resources remain available to the caller.
            bool registryPersisted = false;
            try
            {
                await PersistAsync(cancellationToken).ConfigureAwait(false);
                registryPersisted = true;
                await _physicalStore.DeleteAsync(new PhysicalIndexId(entry.Registration.Id), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // A persistence failure happens before runtime disposal and can be
                // rolled back. A physical deletion failure leaves an unadvertised,
                // recoverable directory and is reported to the caller.
                if (!registryPersisted)
                {
                    lock (_entries)
                        _entries[name] = entry;
                    try { await PersistAsync(cancellationToken).ConfigureAwait(false); }
                    catch { /* retain the original failure */ }
                }
                throw;
            }
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async ValueTask<IndexRuntimeEntry?> UpdateSettingsAsync(string name, MutableIndexSettings settings, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IndexRuntimeEntry? entry;
            lock (_entries)
            {
                if (!_entries.TryGetValue(name, out entry))
                    return null;
            }

            IndexRegistration previous = entry.Registration;
            lock (_entries)
                entry.Registration = previous with { Settings = settings };
            try
            {
                await PersistAsync(cancellationToken).ConfigureAwait(false);
                return entry;
            }
            catch
            {
                lock (_entries)
                    entry.Registration = previous;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        IndexRuntimeEntry[] entries;
        lock (_entries)
            entries = _entries.Values.ToArray();
        foreach (IndexRuntimeEntry entry in entries)
            entry.Handle.Dispose();

        _physicalStore.DisposeAsync().AsTask().GetAwaiter().GetResult();

        _gate.Dispose();
    }

    private ValueTask PersistAsync(CancellationToken cancellationToken)
    {
        IndexRegistration[] registrations;
        lock (_entries)
            registrations = _entries.Values.Select(entry => entry.Registration).ToArray();
        return _store.SaveAsync(new ServerRegistry(registrations, RegistryStore.CurrentFormatVersion), cancellationToken);
    }

    private static void ValidatePersistedRegistrations(IReadOnlyList<IndexRegistration> registrations, string indicesPath)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (IndexRegistration registration in registrations)
        {
            if (!IndexName.IsValid(registration.Name))
                throw new InvalidDataException($"The persisted index name '{registration.Name}' is invalid.");
            if (!names.Add(registration.Name))
                throw new InvalidDataException($"The server registry contains duplicate index name '{registration.Name}'.");
            if (string.IsNullOrWhiteSpace(registration.Id)
                || !Guid.TryParseExact(registration.Id, "N", out _)
                || registration.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || !ids.Add(registration.Id))
                throw new InvalidDataException($"The server registry contains an invalid or duplicate physical index ID for '{registration.Name}'.");
            try
            {
                IndexSchemaValidator.Validate(registration.Schema, registration.Topology, registration.Settings);
                CommunityTopologyValidator.Validate(registration.Topology);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                throw new InvalidDataException($"The persisted schema for index '{registration.Name}' is invalid: {exception.Message}", exception);
            }
            if (!string.Equals(registration.SchemaHash, SchemaHash.Compute(registration.Schema, registration.Topology), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The persisted schema hash for index '{registration.Name}' does not match its schema.");
            string path = Path.Combine(indicesPath, registration.Id);
            if (!Directory.Exists(path))
                throw new InvalidDataException($"The registered index '{registration.Name}' is missing its storage directory.");
        }
    }

    private static LocalIndexDescriptor ToDescriptor(IndexRegistration registration) =>
        new(new PhysicalIndexId(registration.Id), registration.Schema, registration.SchemaHash, registration.Settings, registration.Topology);
}

using Rowles.LeanCorpus.Server.Core.Storage;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Owns registered local indices and prevents implicit creation.</summary>
internal sealed class LocalIndexRegistry : IDisposable
{
    private readonly string _indicesPath;
    private readonly RegistryStore _store;
    private readonly Dictionary<string, IndexRuntimeEntry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private LocalIndexRegistry(string dataRoot)
    {
        _indicesPath = Path.Combine(dataRoot, "indices");
        _store = new RegistryStore(dataRoot);
    }

    internal static async ValueTask<LocalIndexRegistry> OpenAsync(string dataRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(dataRoot);
        LocalIndexRegistry registry = new(dataRoot);
        Directory.CreateDirectory(registry._indicesPath);

        ServerRegistry persisted = await registry._store.LoadAsync(cancellationToken).ConfigureAwait(false);
        foreach (IndexRegistration registration in persisted.Indices)
            registry._entries.Add(registration.Name, new IndexRuntimeEntry(registration, new IndexRuntime(Path.Combine(registry._indicesPath, registration.Id))));

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
            if (_entries.ContainsKey(registration.Name))
                return null;

            string path = Path.Combine(_indicesPath, registration.Id);
            Directory.CreateDirectory(path);
            IndexRuntime runtime = new(path);
            IndexRuntimeEntry entry = new(registration, runtime);
            _entries.Add(registration.Name, entry);

            try
            {
                await PersistAsync(cancellationToken).ConfigureAwait(false);
                return entry;
            }
            catch
            {
                _entries.Remove(registration.Name);
                runtime.Dispose();
                Directory.Delete(path, true);
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
            if (!_entries.Remove(name, out IndexRuntimeEntry? entry))
                return false;

            await PersistAsync(cancellationToken).ConfigureAwait(false);
            entry.Runtime.Dispose();
            Directory.Delete(Path.Combine(_indicesPath, entry.Registration.Id), true);
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
            if (!_entries.TryGetValue(name, out IndexRuntimeEntry? entry))
                return null;

            entry.Registration = entry.Registration with { Settings = settings };
            await PersistAsync(cancellationToken).ConfigureAwait(false);
            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        foreach (IndexRuntimeEntry entry in _entries.Values)
            entry.Runtime.Dispose();

        _gate.Dispose();
    }

    private ValueTask PersistAsync(CancellationToken cancellationToken) =>
        _store.SaveAsync(new ServerRegistry(_entries.Values.Select(entry => entry.Registration).ToArray()), cancellationToken);
}

namespace Rowles.LeanCorpus.Server.Core.Runtime;

internal sealed class IdempotencyStore(int capacity)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    internal bool TryGet(string key, string fingerprint, out Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents.BulkDocumentsResponse? result, out bool conflict)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out Entry? entry))
            {
                result = null;
                conflict = false;
                return false;
            }
            conflict = !string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal);
            result = conflict ? null : entry.Result;
            return true;
        }
    }

    internal void Add(string key, string fingerprint, Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents.BulkDocumentsResponse result)
    {
        lock (_gate)
        {
            if (_entries.ContainsKey(key))
                return;
            _entries[key] = new Entry(fingerprint, result);
            _order.Enqueue(key);
            while (_entries.Count > capacity && _order.TryDequeue(out string? oldest))
                _entries.Remove(oldest);
        }
    }

    private sealed record Entry(string Fingerprint, Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents.BulkDocumentsResponse Result);
}

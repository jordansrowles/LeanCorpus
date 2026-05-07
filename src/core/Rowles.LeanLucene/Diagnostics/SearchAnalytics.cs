using System.Text.Json;
using System.Threading.Channels;
using Rowles.LeanLucene.Serialization;

namespace Rowles.LeanLucene.Diagnostics;

/// <summary>
/// Thread-safe ring buffer of per-search events. Events are captured in
/// <see cref="Search.Searcher.IndexSearcher.Search(Search.Query, int)"/> when wired via
/// <see cref="Search.Searcher.IndexSearcherConfig.SearchAnalytics"/>.
/// </summary>
public sealed class SearchAnalytics
{
    private readonly Channel<SearchEvent> _channel;
    private readonly int _capacity;
    private readonly Lock _readLock = new();

    /// <summary>Creates a new analytics buffer with the given capacity (drop-oldest on overflow).</summary>
    public SearchAnalytics(int capacity = 1000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _channel = Channel.CreateBounded<SearchEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });
    }

    /// <summary>Records a search event. Called internally by IndexSearcher.</summary>
    internal void Record(Search.Query query, TimeSpan elapsed, int totalHits, bool cacheHit)
    {
        var evt = new SearchEvent
        {
            Timestamp = DateTime.UtcNow,
            QueryType = query.GetType().Name,
            Query = query.ToString() ?? query.GetType().Name,
            ElapsedMs = Math.Round(elapsed.TotalMilliseconds, 2),
            TotalHits = totalHits,
            CacheHit = cacheHit
        };

        _channel.Writer.TryWrite(evt);
    }

    /// <summary>Returns up to <paramref name="count"/> most recent events.</summary>
    public List<SearchEvent> GetRecentEvents(int count)
    {
        var events = new List<SearchEvent>(Math.Min(count, _capacity));
        lock (_readLock)
        {
            while (events.Count < count && _channel.Reader.TryRead(out var evt))
                events.Add(evt);
        }
        return events;
    }

    /// <summary>Returns all buffered events as an enumerable, draining the buffer.</summary>
    public IEnumerable<SearchEvent> DrainEvents()
    {
        while (_channel.Reader.TryRead(out var evt))
            yield return evt;
    }

    /// <summary>Exports all buffered events as JSON to the given writer, draining the buffer.</summary>
    public void ExportJson(TextWriter writer)
    {
        writer.Write('[');
        bool first = true;
        foreach (var evt in DrainEvents())
        {
            if (!first) writer.Write(',');
            writer.Write(JsonSerializer.Serialize(evt, LeanLuceneJsonContext.Default.SearchEvent));
            first = false;
        }
        writer.Write(']');
    }
}

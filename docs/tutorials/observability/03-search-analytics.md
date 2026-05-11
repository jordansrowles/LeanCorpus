# Search analytics

`SearchAnalytics` is an in-memory ring buffer of recent search events. Older
entries are dropped when the buffer is full.

## Set up

```csharp
using Rowles.LeanCorpus.Diagnostics;

var analytics = new SearchAnalytics(capacity: 1000);
var config = new IndexSearcherConfig { SearchAnalytics = analytics };
```

## Read recent events

```csharp
var recent = analytics.GetRecentEvents(count: 50);
foreach (var e in recent)
    Console.WriteLine($"{e.Timestamp:O} {e.QueryType} {e.ElapsedMs}ms hits={e.TotalHits} cache={e.CacheHit}");
```

`GetRecentEvents` and `DrainEvents` consume entries; events read once are not
returned by subsequent calls.

## Export as JSON

```csharp
using var writer = new StreamWriter("./events.json");
analytics.ExportJson(writer);
```

Writes a JSON array. The buffer is drained.

## Event fields

`Timestamp`, `QueryType`, `Query`, `ElapsedMs`, `TotalHits`, `CacheHit`.

## See also

- <xref:Rowles.LeanCorpus.Diagnostics.SearchAnalytics>
- <xref:Rowles.LeanCorpus.Diagnostics.SearchEvent>
